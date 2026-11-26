using System.Text.Json;

namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Guarded integration gate for autonomous 9to1 Build tasks. A commit is prepared immutably,
/// recorded as a durable intent, then installed with a compare-and-swap that only accepts the
/// task's exact parent. The intent makes retries after a process crash idempotent.
/// </summary>
public sealed class DeveloperGuardedIntegrator
{
    private sealed record IntegrationIntent(
        string MissionId,
        string TaskId,
        string BranchName,
        string ParentRevision,
        string CommitRevision,
        string DiffSha256,
        IReadOnlyList<string> ChangedPaths,
        IReadOnlyList<string> RequirementIds,
        string WorkerModelId,
        string? ReviewerModelId,
        DateTimeOffset IntegratedAt);

    private static readonly JsonSerializerOptions IntentJson = new() { WriteIndented = true };

    public async Task<DeveloperIntegrationReceipt> IntegrateTaskAsync(
        DeveloperAutonomousMission mission,
        DeveloperMissionTask task,
        string workerModelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(task.WorktreePath) || !Directory.Exists(task.WorktreePath))
            throw new InvalidOperationException("Task worktree is unavailable for guarded integration.");

        var worktree = task.WorktreePath;
        var branchRef = GetSafeBranchRef(mission.IntegrationBranch);
        var intentPath = await GetIntentPathAsync(mission.RepositoryRoot, mission.MissionId, task.TaskId, cancellationToken).ConfigureAwait(false);

        // A prior invocation may have created the immutable commit and intent before dying.
        // Reconcile it before examining the now-empty (or still-dirty) worktree diff.
        var priorIntent = ReadIntent(intentPath);
        if (priorIntent is not null)
            return await ReconcileIntentAsync(mission, task, priorIntent, branchRef, cancellationToken).ConfigureAwait(false);

        var (changedPaths, _, currentDiffSha) = await DeveloperAutonomousWorkspaceTools
            .CaptureDiffAsync(worktree, cancellationToken).ConfigureAwait(false);
        if (changedPaths.Count == 0)
            throw new InvalidOperationException("Cannot integrate: no modified or added files exist in the worktree.");

        if (task.Verification is not { BuildSucceeded: true, TestsSucceeded: true } verification)
            throw new InvalidOperationException("Cannot integrate: guarded build and test verification has not passed.");
        if (!StringComparer.Ordinal.Equals(verification.SourceDiffSha256, currentDiffSha))
            throw new InvalidOperationException("Source diff changed after verification; rerun build and tests before integration.");

        if (mission.RequireSupervisorReview)
        {
            if (task.Review is not { Approved: true } review)
                throw new InvalidOperationException("Cannot integrate: independent supervisor review approval is required.");
            if (!StringComparer.Ordinal.Equals(review.DiffSha256, currentDiffSha))
                throw new InvalidOperationException("Source diff changed after supervisor review; request fresh review before integration.");
        }

        var allowed = task.AllowedSourcePaths.Concat(task.AllowedTestPaths)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').Trim().TrimEnd('/'))
            .ToArray();
        if (allowed.Length > 0)
        {
            foreach (var changed in changedPaths)
            {
                if (!IsWithinAllowedScope(changed, allowed))
                    throw new UnauthorizedAccessException($"Out-of-scope file '{changed}' blocks guarded integration.");
            }
        }

        // git commit includes the entire index. Validate every staged path (including paths hidden
        // from the normal diff listing as generated artifacts) before staging or constructing a tree.
        var staged = await GetStagedPathsAsync(worktree, cancellationToken).ConfigureAwait(false);
        foreach (var path in staged)
        {
            if (DeveloperAutonomousWorkspaceTools.IsIgnoredBuildArtifactPath(path))
                throw new UnauthorizedAccessException($"Pre-staged generated/vendor path '{path}' blocks guarded integration.");
            if (allowed.Length > 0 && !IsWithinAllowedScope(path, allowed))
                throw new UnauthorizedAccessException($"Pre-staged out-of-scope file '{path}' blocks guarded integration.");
        }

        var unstagedCheck = await DeveloperAutonomousWorkspaceTools
            .RunGitDetailedAsync(worktree, ["diff", "--check"], 30, cancellationToken).ConfigureAwait(false);
        var stagedCheck = await DeveloperAutonomousWorkspaceTools
            .RunGitDetailedAsync(worktree, ["diff", "--cached", "--check"], 30, cancellationToken).ConfigureAwait(false);
        if (unstagedCheck.ExitCode != 0 || stagedCheck.ExitCode != 0)
            throw new InvalidOperationException($"Patch formatting check failed:\n{unstagedCheck.StdOut}{unstagedCheck.StdErr}{stagedCheck.StdOut}{stagedCheck.StdErr}");

        var parentRev = await DeveloperAutonomousWorkspaceTools.ResolveHeadRevisionAsync(worktree, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(task.BaseRevision)
            && !StringComparer.Ordinal.Equals(task.BaseRevision, parentRev))
            throw new InvalidOperationException("Task worktree HEAD no longer matches its verified base revision.");

        var branchCheck = await DeveloperAutonomousWorkspaceTools.RunGitDetailedAsync(
            mission.RepositoryRoot, ["rev-parse", "--verify", branchRef], 30, cancellationToken).ConfigureAwait(false);
        if (branchCheck.ExitCode != 0)
            throw new InvalidOperationException($"Integration branch '{mission.IntegrationBranch}' is missing.");
        var branchParent = branchCheck.StdOut.Trim();
        if (!StringComparer.Ordinal.Equals(branchParent, parentRev))
            throw new InvalidOperationException("Integration branch advanced after this task was based; refusing to replace a newer branch tip.");

        // Stage only verified paths, then build an immutable commit from the resulting tree.
        // The staged-path check above ensures no pre-existing index entry can escape task scope.
        var addArgs = new List<string> { "add", "--" };
        addArgs.AddRange(changedPaths);
        await DeveloperAutonomousWorkspaceTools.RunGitAsync(worktree, addArgs, 30, cancellationToken).ConfigureAwait(false);
        var tree = await DeveloperAutonomousWorkspaceTools.RunGitAsync(worktree, ["write-tree"], 30, cancellationToken).ConfigureAwait(false);
        var commitMessage = BuildCommitMessage(mission, task, workerModelId);
        var commitRev = await DeveloperAutonomousWorkspaceTools.RunGitAsync(
            worktree,
            ["-c", "user.name=9to1 Build Autonomous", "-c", "user.email=build-autonomous@9to1.local",
             "commit-tree", tree, "-p", parentRev, "-m", commitMessage],
            30,
            cancellationToken).ConfigureAwait(false);

        var intent = new IntegrationIntent(
            mission.MissionId, task.TaskId, mission.IntegrationBranch, parentRev, commitRev,
            currentDiffSha, changedPaths, task.RecordIds, workerModelId,
            task.Review?.ReviewerModelId, DateTimeOffset.UtcNow);
        WriteIntent(intentPath, intent);
        return await ReconcileIntentAsync(mission, task, intent, branchRef, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DeveloperIntegrationReceipt> ReconcileIntentAsync(
        DeveloperAutonomousMission mission,
        DeveloperMissionTask task,
        IntegrationIntent intent,
        string branchRef,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(intent.MissionId, mission.MissionId)
            || !StringComparer.Ordinal.Equals(intent.TaskId, task.TaskId)
            || !StringComparer.Ordinal.Equals(intent.BranchName, mission.IntegrationBranch))
            throw new InvalidOperationException("Stored integration intent does not match this mission task.");

        var parents = await DeveloperAutonomousWorkspaceTools.RunGitDetailedAsync(
            mission.RepositoryRoot, ["rev-list", "--parents", "-n", "1", intent.CommitRevision], 30, cancellationToken).ConfigureAwait(false);
        var parentFields = parents.StdOut.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parents.ExitCode != 0 || parentFields.Length != 2
            || !StringComparer.Ordinal.Equals(parentFields[0], intent.CommitRevision)
            || !StringComparer.Ordinal.Equals(parentFields[1], intent.ParentRevision))
            throw new InvalidOperationException("Stored integration intent commit does not have the recorded task parent.");

        var branch = await DeveloperAutonomousWorkspaceTools.RunGitDetailedAsync(
            mission.RepositoryRoot, ["rev-parse", "--verify", branchRef], 30, cancellationToken).ConfigureAwait(false);
        if (branch.ExitCode != 0)
            throw new InvalidOperationException("Stored integration intent cannot be reconciled because its branch is missing.");
        var branchTip = branch.StdOut.Trim();
        if (!StringComparer.Ordinal.Equals(branchTip, intent.CommitRevision))
        {
            if (!StringComparer.Ordinal.Equals(branchTip, intent.ParentRevision))
            {
                var ancestor = await DeveloperAutonomousWorkspaceTools.RunGitDetailedAsync(
                    mission.RepositoryRoot, ["merge-base", "--is-ancestor", intent.CommitRevision, branchTip], 30, cancellationToken).ConfigureAwait(false);
                if (ancestor.ExitCode == 0)
                    return ToReceipt(intent);
                throw new InvalidOperationException("Integration branch moved away from the stored intent parent; refusing a non-fast-forward recovery.");
            }

            // The immutable commit was created only after verification/review and the intent was
            // atomically recorded before this ref update. Later worktree state is irrelevant here:
            // replay exactly the recorded object, including across staged/new-file representation changes.
            await DeveloperAutonomousWorkspaceTools.RunGitAsync(
                mission.RepositoryRoot, ["update-ref", branchRef, intent.CommitRevision, intent.ParentRevision], 30, cancellationToken).ConfigureAwait(false);
        }

        return ToReceipt(intent);
    }

    private static DeveloperIntegrationReceipt ToReceipt(IntegrationIntent intent) => new(
        intent.MissionId, intent.TaskId, intent.BranchName, intent.ParentRevision,
        intent.CommitRevision, intent.DiffSha256, intent.ChangedPaths, intent.RequirementIds,
        intent.WorkerModelId, intent.ReviewerModelId, intent.IntegratedAt);

    private static async Task<string> GetIntentPathAsync(
        string repositoryRoot, string missionId, string taskId, CancellationToken cancellationToken)
    {
        var common = await DeveloperAutonomousWorkspaceTools.RunGitAsync(
            repositoryRoot, ["rev-parse", "--git-common-dir"], 30, cancellationToken).ConfigureAwait(false);
        var commonPath = Path.GetFullPath(Path.Combine(repositoryRoot, common));
        var key = DeveloperAutonomousMissionStore.Sha256Hex($"{missionId}\n{taskId}");
        return Path.Combine(commonPath, "9to1-integration-intents", key + ".json");
    }

    private static IntegrationIntent? ReadIntent(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<IntegrationIntent>(File.ReadAllText(path), IntentJson)
            ?? throw new InvalidDataException("Stored integration intent is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Stored integration intent is malformed; manual recovery is required.", ex); }
    }

    private static void WriteIntent(string path, IntegrationIntent intent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(intent, IntentJson));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<IReadOnlyList<string>> GetStagedPathsAsync(string worktree, CancellationToken cancellationToken)
    {
        var result = await DeveloperAutonomousWorkspaceTools.RunGitDetailedAsync(
            worktree, ["diff", "--cached", "--name-only", "-z", "HEAD"], 30, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Could not inspect staged paths: {result.StdErr}");
        return result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsWithinAllowedScope(string changed, IReadOnlyList<string> allowed) => allowed.Any(a =>
        changed.Equals(a, StringComparison.Ordinal) || changed.StartsWith(a + "/", StringComparison.Ordinal));

    private static string GetSafeBranchRef(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName) || branchName.StartsWith("-", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid integration branch name.");
        var branchRef = $"refs/heads/{branchName}";
        if (branchRef.Equals("refs/heads/main", StringComparison.OrdinalIgnoreCase)
            || branchRef.Equals("refs/heads/master", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Guarded integration refuses direct updates to main/master.");
        return branchRef;
    }

    private static string BuildCommitMessage(DeveloperAutonomousMission mission, DeveloperMissionTask task, string workerModelId)
    {
        var reqs = task.RecordIds.Count > 0 ? string.Join(", ", task.RecordIds) : task.TaskId;
        var subject = $"build(autonomous): {reqs} — {task.Title}";
        if (subject.Length > 100) subject = subject[..97] + "...";
        var summary = string.IsNullOrWhiteSpace(task.Summary) ? task.Focus : task.Summary;
        if (summary.Length > 600) summary = summary[..600];
        return $"{subject}\n\n{summary}\n\n"
            + $"Mission-Id: {mission.MissionId}\nTask-Id: {task.TaskId}\nModule: {task.ModuleId}\n"
            + $"Requirements: {reqs}\nWorker-Model: {workerModelId}\n"
            + $"Reviewer-Model: {task.Review?.ReviewerModelId ?? "none"}\n"
            + $"Tests-Passed: {task.Verification?.TestsPassed ?? 0}";
    }
}
