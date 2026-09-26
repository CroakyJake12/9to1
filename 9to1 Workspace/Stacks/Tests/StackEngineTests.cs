using Xunit;

namespace HavenOS.Apps.Stacks.Tests;

public sealed class StackEngineTests
{
    private static readonly StackActor Admin = new("maintainer", Enum.GetValues<StackCapability>().ToHashSet(), TwoFactorVerified: true);
    private static readonly StackActor Contributor = new("contributor", new HashSet<StackCapability> { StackCapability.ViewSource, StackCapability.Contribute, StackCapability.CreateDomain });

    [Fact]
    public async Task ProjectAndDomainIdsSurviveRenameAndReopen()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        StackProjectSummary before = await engine.GetProjectSummaryAsync(Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(before.ProjectId, "Release branch", Admin);
        await engine.RenameDomainAsync(branch.Id, "Release branch renamed", Admin);

        StackDomainSnapshot reopened = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync();
        IReadOnlyList<StackDomainSnapshot> domains = await new StackEngine(new JsonFileStackProjectStore(directory)).GetLineageAsync();

        Assert.Equal(before.ProjectId, reopened.ProjectId);
        Assert.Contains(domains, domain => domain.Id == branch.Id && domain.Name == "Release branch renamed");
        Assert.Equal(StackDomainKind.Main, reopened.Kind);
        Assert.Equal("main", reopened.Name);
    }

    [Fact]
    public async Task HierarchyAcceptsMainBranchTwigLeafAndRejectsIllegalParent()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync()).Id;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        StackDomainSnapshot twig = await engine.CreateTwigAsync(branch.Id, "Twig", Admin);
        StackDomainSnapshot leaf = await engine.CreateLeafAsync(twig.Id, "Leaf", Admin);

        StackFailureException error = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CreateTwigAsync(mainId, "Invalid", Admin));

        Assert.Equal(StackDomainKind.Branch, branch.Kind);
        Assert.Equal(StackDomainKind.Twig, twig.Kind);
        Assert.Equal(StackDomainKind.Leaf, leaf.Kind);
        Assert.Equal(StackFailureCode.InvalidHierarchy, error.Code);
        Assert.Equal(project.ProjectId, branch.ProjectId);
    }

    [Fact]
    public async Task MainChangeCascadesThroughBranchTwigAndLeafWithoutInflatingOwnedCounts()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync()).Id;
        await engine.ApplyChangeAsync(mainId, Upsert("shared.txt", "v1"), Admin);
        await engine.CreateCommitAsync(mainId, "Add shared source", Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        StackDomainSnapshot twig = await engine.CreateTwigAsync(branch.Id, "Twig", Admin);
        StackDomainSnapshot leaf = await engine.CreateLeafAsync(twig.Id, "Leaf", Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("shared.txt", "v2"), Admin);
        await engine.CreateCommitAsync(mainId, "Update shared source", Admin);

        StackEffectiveTreeSnapshot effective = await engine.GetEffectiveTreeAsync(leaf.Id, Admin);
        StackDomainChanges changes = await engine.GetDomainChangesAsync(leaf.Id, Admin);

        Assert.Equal("v2", Text(effective.Files["shared.txt"]));
        Assert.Equal(0, changes.Added);
        Assert.Equal(0, changes.Modified);
        Assert.Equal(0, changes.Deleted);
    }

    [Fact]
    public async Task CascadeConflictPausesOnlyAffectedPathAndKeepsTombstone()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync()).Id;
        await engine.ApplyChangeAsync(mainId, Upsert("conflict.txt", "base"), Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("delete.txt", "base"), Admin);
        await engine.CreateCommitAsync(mainId, "Seed", Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        await engine.ApplyChangeAsync(branch.Id, Upsert("conflict.txt", "local"), Admin);
        await engine.ApplyChangeAsync(branch.Id, new StackMutation(StackMutationKind.Delete, "delete.txt"), Admin);
        await engine.CreateCommitAsync(branch.Id, "Local work", Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("conflict.txt", "incoming"), Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("delete.txt", "incoming"), Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("safe.txt", "cascaded"), Admin);
        await engine.CreateCommitAsync(mainId, "Upstream change", Admin);

        StackEffectiveTreeSnapshot effective = await engine.GetEffectiveTreeAsync(branch.Id, Admin);
        IReadOnlyList<StackConflict> conflicts = await engine.GetConflictsAsync(branch.Id, Admin);

        Assert.Equal("local", Text(effective.Files["conflict.txt"]));
        Assert.False(effective.Files.ContainsKey("delete.txt"));
        Assert.Equal("cascaded", Text(effective.Files["safe.txt"]));
        Assert.Contains(conflicts, conflict => conflict.Path == "conflict.txt" && !conflict.IsResolved);
        Assert.Contains(conflicts, conflict => conflict.Path == "delete.txt" && !conflict.IsResolved);
    }

    [Fact]
    public async Task TrackedRenameWithUpstreamEditBecomesExplicitConflict()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync()).Id;
        await engine.ApplyChangeAsync(mainId, Upsert("old.txt", "base"), Admin);
        await engine.CreateCommitAsync(mainId, "Seed", Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        await engine.ApplyChangeAsync(branch.Id, new StackMutation(StackMutationKind.Rename, "old.txt", RenameTo: "new.txt"), Admin);
        await engine.CreateCommitAsync(branch.Id, "Rename", Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("old.txt", "upstream edit"), Admin);
        await engine.CreateCommitAsync(mainId, "Upstream edit", Admin);

        StackEffectiveTreeSnapshot effective = await engine.GetEffectiveTreeAsync(branch.Id, Admin);
        IReadOnlyList<StackConflict> conflicts = await engine.GetConflictsAsync(branch.Id, Admin);

        Assert.Contains(conflicts, conflict => conflict.Path == "old.txt" && !conflict.IsResolved);
        Assert.True(effective.Files.ContainsKey("new.txt"));
        Assert.False(effective.Files.ContainsKey("old.txt"));
    }

    [Fact]
    public async Task ConflictProposalRequiresReviewAndSupportsRejectAcceptWithoutMergeAndMerge()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        (Guid mainId, StackDomainSnapshot branch) = await CreateConflictedBranchAsync(engine, "conflict.txt");
        StackConflict conflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Admin));
        var rejected = await engine.ProposeConflictResolutionAsync(conflict.Id, "First proposal", Resource("rejected"), "dulche", Admin);

        await engine.ReviewConflictProposalAsync(rejected.Id, accept: false, merge: false, Admin);
        Assert.False(Assert.Single(await engine.GetConflictsAsync(branch.Id, Admin)).IsResolved);
        Assert.Equal(StackConflictProposalState.Rejected, (await engine.GetConflictProposalsAsync(Admin)).Single(item => item.Id == rejected.Id).State);

        var acceptedWithoutMerge = await engine.ProposeConflictResolutionAsync(conflict.Id, "Reviewed proposal", Resource("reviewed"), "dulche", Admin);
        await engine.ReviewConflictProposalAsync(acceptedWithoutMerge.Id, accept: true, merge: false, Admin);
        Assert.Equal(StackConflictProposalState.AcceptedWithoutMerge, (await engine.GetConflictProposalsAsync(Admin)).Single(item => item.Id == acceptedWithoutMerge.Id).State);
        Assert.False((await engine.GetConflictsAsync(branch.Id, Admin)).Single(item => item.Id == conflict.Id).MergeCompleted);

        await engine.CreateCommitAsync(branch.Id, "Commit reviewed resolution", Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("conflict.txt", "third upstream"), Admin);
        await engine.CreateCommitAsync(mainId, "Second upstream update", Admin);
        _ = await engine.GetEffectiveTreeAsync(branch.Id, Admin);
        StackConflict nextConflict = (await engine.GetConflictsAsync(branch.Id, Admin)).Single(item => !item.IsResolved);
        var acceptedAndMerged = await engine.ProposeConflictResolutionAsync(nextConflict.Id, "Second reviewed proposal", Resource("merged"), "dulche", Admin);
        await engine.ReviewConflictProposalAsync(acceptedAndMerged.Id, accept: true, merge: true, Admin);

        Assert.Equal(StackConflictProposalState.AcceptedAndMerged, (await engine.GetConflictProposalsAsync(Admin)).Single(item => item.Id == acceptedAndMerged.Id).State);
        Assert.True((await engine.GetConflictsAsync(branch.Id, Admin)).Single(item => item.Id == nextConflict.Id).MergeCompleted);
    }

    [Fact]
    public async Task RootBlocksDescendantEditsAndOwnerUpdatesCascade()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync()).Id;
        await engine.ApplyChangeAsync(mainId, Upsert("engine/core.cs", "v1"), Admin);
        await engine.CreateCommitAsync(mainId, "Seed root content", Admin);
        StackRoot root = await engine.CreateRootAsync(mainId, ["engine"], Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);

        StackFailureException denied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.ApplyChangeAsync(branch.Id, Upsert("engine/core.cs", "tamper"), Admin));
        await engine.UpdateRootAsync(root.Id, [Upsert("engine/core.cs", "v2")], "Update framework", Admin);
        StackEffectiveTreeSnapshot effective = await engine.GetEffectiveTreeAsync(branch.Id, Admin);

        Assert.Equal(StackFailureCode.RootLocked, denied.Code);
        Assert.Equal("v2", Text(effective.Files["engine/core.cs"]));
    }

    [Fact]
    public async Task FreezeAppliesOnlyToExplicitDescendantsAndRequiresAuthorisedThaw()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        StackDomainSnapshot frozenTwig = await engine.CreateTwigAsync(branch.Id, "Frozen", Admin);
        StackDomainSnapshot openTwig = await engine.CreateTwigAsync(branch.Id, "Open", Admin);
        StackFreeze freeze = await engine.CreateFreezeAsync(branch.Id, "module", [frozenTwig.Id], "Stabilize release", null, null, Admin);

        StackFailureException denied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.ApplyChangeAsync(frozenTwig.Id, Upsert("module/file.cs", "blocked"), Admin));
        await engine.ApplyChangeAsync(openTwig.Id, Upsert("module/file.cs", "allowed"), Admin);
        await Assert.ThrowsAsync<StackFailureException>(() => engine.ThawFreezeAsync(freeze.Id, early: true, hasInProgressOperations: true, Admin));
        await engine.ThawFreezeAsync(freeze.Id, early: true, hasInProgressOperations: false, Admin);
        await engine.ApplyChangeAsync(frozenTwig.Id, Upsert("module/file.cs", "unfrozen"), Admin);

        Assert.Equal(StackFailureCode.FreezeActive, denied.Code);
        StackFailureException permission = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.ThawFreezeAsync(freeze.Id, early: true, hasInProgressOperations: false, Contributor));
        Assert.Equal(StackFailureCode.PermissionDenied, permission.Code);
    }

    [Fact]
    public async Task SubrootPreventsOverlappingClaimsAndCompletesOnlyAfterChecksAndApprovals()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync()).Id;
        StackFailureException mainError = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CreateSubrootAsync(mainId, [Guid.NewGuid()], [], 0, Admin));
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        StackRoot root = await engine.CreateRootAsync(branch.Id, ["shared"], Admin);
        StackSubroot subroot = await engine.CreateSubrootAsync(branch.Id, [root.Id], ["build"], 1, Admin);
        StackFailureException overlap = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CreateSubrootAsync(branch.Id, [root.Id], [], 0, Admin));
        await engine.RecordSubrootChangesAsync(subroot.Id, [Upsert("shared/api.cs", "contract")], Admin);
        StackFailureException gate = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CompleteSubrootAsync(subroot.Id, "Complete Root update", Admin));
        await engine.RecordSubrootCheckAsync(subroot.Id, "build", true, Admin);
        await engine.ApproveSubrootAsync(subroot.Id, new StackActor("reviewer", Admin.Capabilities, true));
        StackRevision revision = await engine.CompleteSubrootAsync(subroot.Id, "Complete Root update", Admin);

        Assert.Equal(StackFailureCode.InvalidHierarchy, mainError.Code);
        Assert.Equal(StackFailureCode.RootClaimed, overlap.Code);
        Assert.Equal(StackFailureCode.RequiredCheckFailed, gate.Code);
        Assert.True(revision.Sequence > 1);
        StackSubroot laterClaim = await engine.CreateSubrootAsync(branch.Id, [root.Id], [], 0, Admin);
        Assert.True(laterClaim.IsActive);
    }

    [Fact]
    public async Task ManifestValidatesManagedFoldersAndRejectsUnknownSchemaWithoutOverwriting()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        StackProjectSummary summary = await engine.GetProjectSummaryAsync(Admin);
        string manifestPath = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string original = await File.ReadAllTextAsync(manifestPath);
        string future = original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999", StringComparison.Ordinal);
        await File.WriteAllTextAsync(manifestPath, future);

        StackFailureException error = await Assert.ThrowsAsync<StackFailureException>(
            () => new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync());

        Assert.Equal(StackFailureCode.SchemaVersionUnsupported, error.Code);
        Assert.Equal(future, await File.ReadAllTextAsync(manifestPath));
        Assert.NotEqual(Guid.Empty, summary.ProjectId);
    }

    [Fact]
    public async Task ManagedPathsAndUnsafeProjectPathsAreRejected()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        Guid main = (await engine.OpenProjectAsync()).Id;
        foreach (string path in new[] { "../escape.txt", ".git/config", ".branches/stack.manifest.json", "C:/outside.txt" })
        {
            StackFailureException error = await Assert.ThrowsAsync<StackFailureException>(
                () => engine.ApplyChangeAsync(main, Upsert(path, "unsafe"), Admin));
            Assert.Equal(StackFailureCode.InvalidPath, error.Code);
        }
    }

    [Fact]
    public async Task MainCannotBePrunedAndPruneRequiresConfiguredTwoFactor()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        Guid main = (await engine.OpenProjectAsync()).Id;
        StackFailureException mainError = await Assert.ThrowsAsync<StackFailureException>(() => engine.PruneDomainAsync(main, Admin));
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        var withoutTwoFactor = new StackActor("maintainer-no-2fa", Admin.Capabilities, TwoFactorVerified: false);
        StackFailureException twoFactorError = await Assert.ThrowsAsync<StackFailureException>(() => engine.PruneDomainAsync(branch.Id, withoutTwoFactor));

        Assert.Equal(StackFailureCode.InvalidHierarchy, mainError.Code);
        Assert.Equal(StackFailureCode.PermissionDenied, twoFactorError.Code);
    }

    private static async Task<StackEngine> CreateProjectAsync(string directory)
    {
        var engine = new StackEngine(new JsonFileStackProjectStore(directory));
        await engine.CreateProjectAsync(new StackProjectConfiguration("Stacks tests", StackStorageMode.Files, directory), Admin);
        return engine;
    }

    private static StackMutation Upsert(string path, string content) => new(StackMutationKind.Upsert, path, Resource(content));
    private static StackResource Resource(string content) => new(System.Text.Encoding.UTF8.GetBytes(content));
    private static string Text(StackResource resource) => System.Text.Encoding.UTF8.GetString(resource.Content);
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "nine-to-one-stacks-tests", Guid.NewGuid().ToString("N"));

    private static async Task<(Guid MainId, StackDomainSnapshot Branch)> CreateConflictedBranchAsync(StackEngine engine, string path)
    {
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync()).Id;
        await engine.ApplyChangeAsync(mainId, Upsert(path, "base"), Admin);
        await engine.CreateCommitAsync(mainId, "Seed", Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        await engine.ApplyChangeAsync(branch.Id, Upsert(path, "local"), Admin);
        await engine.CreateCommitAsync(branch.Id, "Local", Admin);
        await engine.ApplyChangeAsync(mainId, Upsert(path, "incoming"), Admin);
        await engine.CreateCommitAsync(mainId, "Incoming", Admin);
        _ = await engine.GetEffectiveTreeAsync(branch.Id, Admin);
        return (mainId, branch);
    }
}
