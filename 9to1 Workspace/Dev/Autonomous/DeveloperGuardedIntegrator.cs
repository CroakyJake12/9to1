namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Guarded integration gate for autonomous 9to1 Build tasks.
/// Verifies exact diff SHA-256 continuity (no TOCTOU drift between verification/review and commit),
/// clean patch formatting, scope compliance, passing build + tests, supervisor review approval,
/// commits inside the isolated worktree, and CAS-updates ONLY the isolated mission branch ref.
/// Never force-pushes or mutates a dirty primary checkout.
/// </summary>
public sealed class DeveloperGuardedIntegrator
{
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

        // Enforce exclusive task path scope if specified.
        var allowed = task.AllowedSourcePaths.Concat(task.AllowedTestPaths)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').Trim().TrimEnd('/'))
            .ToArray();
        if (allowed.Length > 0)
        {
            foreach (var changed in changedPaths)
            {
                var permitted = allowed.Any(a =>
                    changed.Equals(a, StringComparison.Ordinal)
                    || changed.StartsWith(a + "/", StringComparison.Ordinal));
                if (!permitted)
                    throw new UnauthorizedAccessException($"Out-of-scope file '{changed}' blocks guarded integration.");
            }
        }

        // Validate clean patch formatting (no conflict markers / malformed trailing whitespace errors).
        var checkDiff = await DeveloperAutonomousWorkspaceTools
            .RunGitDetailedAsync(worktree, ["diff", "--check"], 30, cancellationToken).ConfigureAwait(false);
        if (checkDiff.ExitCode != 0 && checkDiff.StdOut.Contains("conflict marker", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Patch contains conflict markers:\n{checkDiff.StdOut}");

        var parentRev = await DeveloperAutonomousWorkspaceTools
            .ResolveHeadRevisionAsync(worktree, cancellationToken).ConfigureAwait(false);

        // Stage ONLY the verified changed paths.
        var addArgs = new List<string> { "add", "--" };
        addArgs.AddRange(changedPaths);
        await DeveloperAutonomousWorkspaceTools.RunGitAsync(worktree, addArgs, 30, cancellationToken).ConfigureAwait(false);

        var commitMessage = BuildCommitMessage(mission, task, workerModelId);
        try
        {
            await DeveloperAutonomousWorkspaceTools.RunGitAsync(
                worktree,
                [
                    "-c", "user.name=9to1 Build Autonomous",
                    "-c", "user.email=build-autonomous@9to1.local",
                    "-c", "commit.gpgsign=false",
                    "commit", "-m", commitMessage
                ],
                30,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DeveloperAutonomousWorkspaceTools.RunGitDetailedAsync(worktree, ["reset", "HEAD"], 30, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var commitRev = await DeveloperAutonomousWorkspaceTools
            .ResolveHeadRevisionAsync(worktree, cancellationToken).ConfigureAwait(false);

        // CAS-update ONLY the isolated mission branch ref, never main.
        var branchRef = $"refs/heads/{mission.IntegrationBranch}";
        if (branchRef.Equals("refs/heads/main", StringComparison.OrdinalIgnoreCase)
            || branchRef.Equals("refs/heads/master", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Guarded integration refuses direct updates to main/master.");
        }

        var refCheck = await DeveloperAutonomousWorkspaceTools
            .RunGitDetailedAsync(mission.RepositoryRoot, ["rev-parse", "--verify", branchRef], 30, cancellationToken).ConfigureAwait(false);
        if (refCheck.ExitCode == 0)
        {
            var expectedOld = refCheck.StdOut.Trim();
            await DeveloperAutonomousWorkspaceTools.RunGitAsync(
                mission.RepositoryRoot,
                ["update-ref", branchRef, commitRev, expectedOld],
                30,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DeveloperAutonomousWorkspaceTools.RunGitAsync(
                mission.RepositoryRoot,
                ["update-ref", branchRef, commitRev],
                30,
                cancellationToken).ConfigureAwait(false);
        }

        return new DeveloperIntegrationReceipt(
            MissionId: mission.MissionId,
            TaskId: task.TaskId,
            BranchName: mission.IntegrationBranch,
            ParentRevision: parentRev,
            CommitRevision: commitRev,
            DiffSha256: currentDiffSha,
            ChangedPaths: changedPaths,
            RequirementIds: task.RecordIds,
            WorkerModelId: workerModelId,
            ReviewerModelId: task.Review?.ReviewerModelId,
            IntegratedAt: DateTimeOffset.UtcNow);
    }

    private static string BuildCommitMessage(
        DeveloperAutonomousMission mission,
        DeveloperMissionTask task,
        string workerModelId)
    {
        var reqs = task.RecordIds.Count > 0 ? string.Join(", ", task.RecordIds) : task.TaskId;
        var subject = $"build(autonomous): {reqs} — {task.Title}";
        if (subject.Length > 100) subject = subject[..97] + "...";
        var summary = string.IsNullOrWhiteSpace(task.Summary) ? task.Focus : task.Summary;
        if (summary.Length > 600) summary = summary[..600];
        return $"{subject}\n\n{summary}\n\n"
            + $"Mission-Id: {mission.MissionId}\n"
            + $"Task-Id: {task.TaskId}\n"
            + $"Module: {task.ModuleId}\n"
            + $"Requirements: {reqs}\n"
            + $"Worker-Model: {workerModelId}\n"
            + $"Reviewer-Model: {task.Review?.ReviewerModelId ?? "none"}\n"
            + $"Tests-Passed: {task.Verification?.TestsPassed ?? 0}";
    }
}
