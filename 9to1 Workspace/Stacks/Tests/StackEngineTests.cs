using System.Text.Json.Nodes;
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

        StackDomainSnapshot reopened = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync(Admin);
        IReadOnlyList<StackLineageEntry> domains = (await new StackEngine(new JsonFileStackProjectStore(directory))
            .GetLineagePageAsync(Admin, 0, 200)).Entries;

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
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
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
    public async Task CreateBranch_checks_create_capability_before_project_identity_lookup()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        var unprivileged = new StackActor("read-only", new HashSet<StackCapability> { StackCapability.ViewSource });

        StackFailureException existing = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CreateBranchAsync(projectId, "Denied", unprivileged));
        StackFailureException unknown = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CreateBranchAsync(Guid.NewGuid(), "Denied", unprivileged));

        Assert.Equal(StackFailureCode.PermissionDenied, existing.Code);
        Assert.Equal(existing.Code, unknown.Code);
        Assert.Equal(existing.Message, unknown.Message);
        Assert.Single((await engine.GetLineagePageAsync(Admin, 0, 100)).Entries);
    }

    [Fact]
    public async Task Project_open_and_lineage_reads_require_view_source_capability()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        var noView = new StackActor("no-view", new HashSet<StackCapability>());
        Guid mainId = (await engine.GetProjectSummaryAsync(Admin)).ActiveDomainId;

        StackFailureException openDenied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.OpenProjectAsync(noView));
        StackFailureException lineageDenied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.GetLineagePageAsync(noView, 0, 100));
        StackFailureException proposalPageDenied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.GetConflictProposalsPageAsync(noView, mainId, 0, 100));
        StackFailureException conflictSummaryDenied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.GetConflictSummariesAsync(mainId, noView));
        StackFailureException conflictSnapshotDenied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.GetConflictSnapshotAsync(mainId, noView));
        StackFailureException proposalSnapshotDenied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.GetConflictProposalSnapshotAsync(Guid.NewGuid(), noView));
        StackFailureException domainDenied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.GetDomainSnapshotAsync(mainId, noView));

        Assert.Equal(StackFailureCode.PermissionDenied, openDenied.Code);
        Assert.Equal(openDenied.Code, lineageDenied.Code);
        Assert.Equal(openDenied.Code, proposalPageDenied.Code);
        Assert.Equal(openDenied.Code, conflictSummaryDenied.Code);
        Assert.Equal(openDenied.Code, conflictSnapshotDenied.Code);
        Assert.Equal(openDenied.Code, proposalSnapshotDenied.Code);
        Assert.Equal(openDenied.Code, domainDenied.Code);
        Assert.Equal("main", (await engine.OpenProjectAsync(Admin)).Name);
        Assert.Single((await engine.GetLineagePageAsync(Admin, 0, 100)).Entries);
    }

    [Fact]
    public async Task Lineage_pages_preserve_domain_tree_order_depth_and_bound_results()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
        Guid project = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branchA = await engine.CreateBranchAsync(project, "A", Admin);
        StackDomainSnapshot twig = await engine.CreateTwigAsync(branchA.Id, "Twig", Admin);
        StackDomainSnapshot leaf = await engine.CreateLeafAsync(twig.Id, "Leaf", Admin);
        StackDomainSnapshot branchB = await engine.CreateBranchAsync(project, "B", Admin);

        StackLineagePage first = await engine.GetLineagePageAsync(Admin, 0, 2);
        StackLineagePage second = await engine.GetLineagePageAsync(Admin, 1, 2);
        StackLineagePage last = await engine.GetLineagePageAsync(Admin, 2, 2);
        StackLineagePage located = await engine.GetLineagePageAsync(Admin, 0, 2, leaf.Id, locateRequestedDomain: true);
        StackLineagePage missing = await engine.GetLineagePageAsync(Admin, 0, 2, Guid.NewGuid());
        StackLineagePage missingLocated = await engine.GetLineagePageAsync(Admin, 1, 2, Guid.NewGuid(), locateRequestedDomain: true);

        Assert.Equal(5, first.TotalCount); Assert.Equal(2, first.Entries.Count);
        Assert.Equal(new[] { main, branchA.Id }, first.Entries.Select(entry => entry.Id));
        Assert.Equal(new[] { 0, 1 }, first.Entries.Select(entry => entry.Depth));
        Assert.Equal(new[] { twig.Id, leaf.Id }, second.Entries.Select(entry => entry.Id));
        Assert.Equal(new[] { 2, 3 }, second.Entries.Select(entry => entry.Depth));
        Assert.Equal(new[] { branchB.Id }, last.Entries.Select(entry => entry.Id));
        Assert.Equal(1, located.PageIndex); Assert.True(located.RequestedDomainFound);
        Assert.Equal(new[] { twig.Id, leaf.Id }, located.Entries.Select(entry => entry.Id));
        Assert.False(missing.RequestedDomainFound); Assert.Equal(2, missing.Entries.Count);
        Assert.False(missingLocated.RequestedDomainFound); Assert.Equal(1, missingLocated.PageIndex);
        Assert.Equal(second.Entries.Select(entry => entry.Id), missingLocated.Entries.Select(entry => entry.Id));
        Assert.Equal(branchB.Id, (await engine.GetDomainSnapshotAsync(branchB.Id, Admin)).Id);
        await Assert.ThrowsAsync<StackFailureException>(() => engine.GetDomainSnapshotAsync(Guid.NewGuid(), Admin));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.GetLineagePageAsync(Admin, 0, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.GetLineagePageAsync(Admin, int.MaxValue, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Domain_change_query_does_not_expose_retained_mutation_content(bool committed)
    {
        var engine = await CreateProjectAsync(NewDirectory());
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
        await engine.ApplyChangeAsync(mainId, Upsert("owned.txt", "original"), Admin);
        if (committed) await engine.CreateCommitAsync(mainId, "Keep owned content", Admin);
        var viewer = new StackActor("read-only viewer", new HashSet<StackCapability> { StackCapability.ViewSource });

        StackDomainChanges changes = await engine.GetDomainChangesAsync(mainId, viewer);
        StackMutation returned = Assert.Single(changes.Changes);
        Array.Fill(returned.Resource!.Content, (byte)'x');

        StackDomainChanges fresh = await engine.GetDomainChangesAsync(mainId, Admin);
        Assert.Equal("original", Text(Assert.Single(fresh.Changes).Resource!));
        Assert.Equal("original", Text((await engine.GetEffectiveTreeAsync(mainId, Admin)).Files["owned.txt"]));
    }

    [Fact]
    public async Task MainChangeCascadesThroughBranchTwigAndLeafWithoutInflatingOwnedCounts()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
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
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
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
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
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
    public async Task Created_proposal_content_does_not_alias_retained_pending_review()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        var (_, branch) = await CreateConflictedBranchAsync(engine, "conflict.txt");
        StackConflict conflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Admin));
        StackResource input = Resource("reviewed");
        StackConflictProposal proposal = await engine.ProposeConflictResolutionAsync(conflict.Id,
            "Reviewed content", input, "dulche", Admin);

        // Neither caller-owned input nor the returned save result owns engine state.
        input.Content[0] = (byte)'X';
        proposal.ProposedResource!.Content[0] = (byte)'Y';
        StackConflictProposal retained = (await engine.GetConflictProposalsAsync(Admin)).Single(item => item.Id == proposal.Id);
        Assert.Equal("reviewed", Text(retained.ProposedResource!));

        await engine.ReviewConflictProposalAsync(proposal.Id, accept: true, merge: false, Admin);
        Assert.Equal("reviewed", Text((await engine.GetEffectiveTreeAsync(branch.Id, Admin)).Files["conflict.txt"]));
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
    public async Task Read_only_conflict_and_proposal_queries_detach_resource_bytes_from_the_json_engine_cache()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        var (_, branch) = await CreateConflictedBranchAsync(engine, "detached.txt");
        StackConflict conflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Admin));
        StackConflictProposal proposal = await engine.ProposeConflictResolutionAsync(conflict.Id,
            "Detached proposal", Resource("accepted bytes"), "fixture reviewer", Admin);
        string manifestPath = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] originalManifest = await File.ReadAllBytesAsync(manifestPath);

        StackConflictProposal proposalSnapshot = Assert.Single(await engine.GetConflictProposalsAsync(Admin));
        Array.Fill(proposalSnapshot.ProposedResource!.Content, (byte)'x');
        StackConflict conflictSnapshot = Assert.Single(await engine.GetConflictsAsync(branch.Id, Admin));
        Array.Fill(conflictSnapshot.Local!.Content, (byte)'y');
        StackConflictProposalPage proposalPage = await engine.GetConflictProposalsPageAsync(
            Admin, branch.Id, 0, 10, proposal.Id);
        Assert.Equal(proposal.Id, Assert.Single(proposalPage.Entries).Id);
        Array.Fill(proposalPage.RequestedProposal!.ProposedResource!.Content, (byte)'w');
        StackConflictProposal proposalDetail = await engine.GetConflictProposalSnapshotAsync(proposal.Id, Admin);
        Array.Fill(proposalDetail.ProposedResource!.Content, (byte)'v');

        StackConflictProposal actualProposal = Assert.Single(await engine.GetConflictProposalsAsync(Admin));
        StackConflict actualConflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Admin));

        Assert.Equal("accepted bytes", Text(actualProposal.ProposedResource!));
        Assert.Equal("local", Text(actualConflict.Local!));
        Assert.Equal(originalManifest, await File.ReadAllBytesAsync(manifestPath));
    }

    [Fact]
    public async Task Conflict_proposal_accept_enforces_domain_root_freeze_and_current_path_source_before_any_write()
    {
        string directory = NewDirectory();
        string Absolute(string relative) => Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        var engine = await CreateProjectAsync(directory);
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
        string[] paths = ["root-guard.txt", "freeze-guard.txt", "retired-guard.txt", "stale-guard.txt", "safe.txt"];
        foreach (string path in paths) await engine.ApplyChangeAsync(mainId, Upsert(path, "base " + path), Admin);
        await engine.CreateCommitAsync(mainId, "Seed proposal guard fixtures", Admin);
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Proposal guards", Admin);
        foreach (string path in paths) await engine.ApplyChangeAsync(branch.Id, Upsert(path, "local " + path), Admin);
        await engine.CreateCommitAsync(branch.Id, "Commit proposal conflict sides", Admin);
        await engine.ApplyChangeAsync(branch.Id, Upsert("unrelated.txt", "preserve unrelated working bytes"), Admin);
        foreach (string path in paths) await engine.ApplyChangeAsync(mainId, Upsert(path, "incoming " + path), Admin);
        await engine.CreateCommitAsync(mainId, "Create proposal guard conflicts", Admin);
        _ = await engine.GetEffectiveTreeAsync(branch.Id, Admin);

        var conflicts = (await engine.GetConflictsAsync(branch.Id, Admin)).ToDictionary(conflict => conflict.Path);
        var proposals = new Dictionary<string, StackConflictProposal>(StringComparer.Ordinal);
        foreach (string path in paths)
            proposals.Add(path, await engine.ProposeConflictResolutionAsync(conflicts[path].Id,
                "Review " + path, Resource("proposed " + path), "fixture reviewer", Admin));

        _ = await engine.CreateRootAsync(mainId, ["root-guard.txt"], Admin);
        _ = await engine.CreateFreezeAsync(mainId, "freeze-guard.txt", [branch.Id], "Freeze before proposal review",
            expiresAt: null, expiryCondition: null, actor: Admin);

        async Task AssertRefusedWithoutAnySourceMutationAsync(StackConflictProposal proposal, StackFailureCode expectedCode)
        {
            byte[] manifestBefore = await File.ReadAllBytesAsync(Absolute(JsonFileStackProjectStore.ManifestRelativePath));
            byte[] rootsBefore = await File.ReadAllBytesAsync(Absolute(JsonFileStackProjectStore.RootsRelativePath));
            StackFailureException refused = await Assert.ThrowsAsync<StackFailureException>(
                () => engine.ReviewConflictProposalAsync(proposal.Id, accept: true, merge: false, Admin));
            Assert.Equal(expectedCode, refused.Code);
            Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(Absolute(JsonFileStackProjectStore.ManifestRelativePath)));
            Assert.Equal(rootsBefore, await File.ReadAllBytesAsync(Absolute(JsonFileStackProjectStore.RootsRelativePath)));
        }

        await AssertRefusedWithoutAnySourceMutationAsync(proposals["root-guard.txt"], StackFailureCode.RootLocked);
        await AssertRefusedWithoutAnySourceMutationAsync(proposals["freeze-guard.txt"], StackFailureCode.FreezeActive);

        await engine.ReviewConflictProposalAsync(proposals["safe.txt"].Id, accept: true, merge: false, Admin);
        StackConflict safeConflict = (await engine.GetConflictsAsync(branch.Id, Admin)).Single(conflict => conflict.Id == conflicts["safe.txt"].Id);
        Assert.True(safeConflict.IsResolved); Assert.False(safeConflict.MergeCompleted);
        StackDomainSnapshot safeSnapshot = await engine.GetDomainSnapshotAsync(branch.Id, Admin);
        Assert.Equal("proposed safe.txt", Text(safeSnapshot.WorkingChanges.Single(change => change.Path == "safe.txt").Resource!));
        Assert.Equal("preserve unrelated working bytes", Text(safeSnapshot.WorkingChanges.Single(change => change.Path == "unrelated.txt").Resource!));

        await engine.ApplyChangeAsync(branch.Id, Upsert("stale-guard.txt", "edited after proposal"), Admin);
        await engine.ApplyChangeAsync(mainId, Upsert("stale-guard.txt", "new incoming after proposal"), Admin);
        await engine.CreateCommitAsync(mainId, "Advance only the proposed source path", Admin);
        await AssertRefusedWithoutAnySourceMutationAsync(proposals["stale-guard.txt"], StackFailureCode.RevisionConflict);

        await engine.ReviewConflictProposalAsync(proposals["root-guard.txt"].Id, accept: false, merge: false, Admin);
        Assert.Equal(StackConflictProposalState.Rejected,
            (await engine.GetConflictProposalsAsync(Admin)).Single(proposal => proposal.Id == proposals["root-guard.txt"].Id).State);
        Assert.False((await engine.GetConflictsAsync(branch.Id, Admin)).Single(conflict => conflict.Id == conflicts["root-guard.txt"].Id).IsResolved);

        await engine.PruneDomainAsync(branch.Id, Admin);
        await AssertRefusedWithoutAnySourceMutationAsync(proposals["retired-guard.txt"], StackFailureCode.DomainNotFound);
    }

    [Fact]
    public async Task Created_root_paths_are_detached_from_authoritative_retained_root()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
        await engine.ApplyChangeAsync(mainId, Upsert("protected/file.txt", "authoritative bytes"), Admin);
        await engine.CreateCommitAsync(mainId, "Seed protected source", Admin);
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Protected child", Admin);
        StackRoot created = await engine.CreateRootAsync(mainId, ["protected"], Admin);

        string[] returnedPaths = Assert.IsType<string[]>(created.Paths);
        returnedPaths[0] = "unprotected";

        StackFailureException refused = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.ApplyChangeAsync(branch.Id, Upsert("protected/file.txt", "tampered"), Admin));
        Assert.Equal(StackFailureCode.RootLocked, refused.Code);
        string manifestPath = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        JsonNode persisted = JsonNode.Parse(await File.ReadAllBytesAsync(manifestPath))!;
        JsonObject retainedRoot = persisted["roots"]!.AsArray().Select(node => node!.AsObject())
            .Single(node => Guid.Parse(node["id"]!.GetValue<string>()) == created.Id);
        Assert.Equal("protected", Assert.Single(retainedRoot["paths"]!.AsArray()).GetValue<string>());
        Assert.Equal("authoritative bytes", Text((await engine.GetEffectiveTreeAsync(branch.Id, Admin)).Files["protected/file.txt"]));
    }

    [Fact]
    public async Task RootBlocksDescendantEditsAndOwnerUpdatesCascade()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
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
    public async Task Created_freeze_scope_is_detached_from_authoritative_retained_freeze()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Freeze owner", Admin);
        StackFreeze created = await engine.CreateFreezeAsync(mainId, null, [branch.Id],
            "Keep descendant source frozen", null, null, Admin);

        created.DescendantDomainIds.Clear();
        byte[] manifestBeforeAttempt = await File.ReadAllBytesAsync(Path.Combine(directory,
            JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        StackFailureException denied = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.ApplyChangeAsync(branch.Id, Upsert("protected.txt", "blocked"), Admin));

        Assert.Equal(StackFailureCode.FreezeActive, denied.Code);
        Assert.Equal(manifestBeforeAttempt, await File.ReadAllBytesAsync(Path.Combine(directory,
            JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        var persisted = await new JsonFileStackProjectStore(directory).LoadAsync();
        Assert.Contains(branch.Id, Assert.Single(persisted.Freezes).DescendantDomainIds);
    }

    [Fact]
    public async Task SubrootPreventsOverlappingClaimsAndCompletesOnlyAfterChecksAndApprovals()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
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
    public async Task Created_subroot_result_collections_cannot_change_retained_claims_or_gates()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        _ = await engine.OpenProjectAsync(Admin);
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Subroot owner", Admin);
        StackRoot root = await engine.CreateRootAsync(branch.Id, ["contract"], Admin);
        StackSubroot created = await engine.CreateSubrootAsync(branch.Id, [root.Id], ["build"], 1, Admin);
        Guid subrootId = created.Id;

        Assert.IsType<Guid[]>(created.RootIds)[0] = Guid.NewGuid();
        created.RequiredChecks.Clear();
        created.CompletedChecks.Add("build");
        created.Approvals.Add("unverified caller");
        await engine.RecordSubrootChangesAsync(subrootId, [Upsert("contract/api.cs", "proposal")], Admin);

        StackFailureException overlappingClaim = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CreateSubrootAsync(branch.Id, [root.Id], [], 0, Admin));
        StackFailureException forgedGate = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.CompleteSubrootAsync(subrootId, "Do not accept caller-mutated checks", Admin));
        Assert.Equal(StackFailureCode.RootClaimed, overlappingClaim.Code);
        Assert.Equal(StackFailureCode.RequiredCheckFailed, forgedGate.Code);

        await engine.RecordSubrootCheckAsync(subrootId, "build", passed: true, Admin);
        await engine.ApproveSubrootAsync(subrootId, new StackActor("reviewer", Admin.Capabilities, true));
        StackRevision completed = await engine.CompleteSubrootAsync(subrootId, "Complete protected update", Admin);
        Assert.True(completed.Sequence > 1);
        Assert.Equal("proposal", Text((await engine.GetEffectiveTreeAsync(branch.Id, Admin)).Files["contract/api.cs"]));
    }

    [Fact]
    public async Task Created_revision_changed_paths_are_detached_from_retained_history()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
        await engine.ApplyChangeAsync(mainId, Upsert("owned.txt", "revision content"), Admin);
        StackRevision created = await engine.CreateCommitAsync(mainId, "Retain source revision", Admin);

        Assert.IsType<string[]>(created.ChangedPaths)[0] = "forged.txt";
        _ = await engine.GetEffectiveTreeAsync(mainId, Admin);

        StackManifest reopened = await new JsonFileStackProjectStore(directory).LoadAsync();
        StackRevision retained = reopened.Revisions.Single(revision => revision.Id == created.Id);
        Assert.Equal("owned.txt", Assert.Single(retained.ChangedPaths));
        Assert.Equal("revision content", Text((await engine.GetEffectiveTreeAsync(mainId, Admin)).Files["owned.txt"]));
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
            () => new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync(Admin));

        Assert.Equal(StackFailureCode.SchemaVersionUnsupported, error.Code);
        Assert.Equal(future, await File.ReadAllTextAsync(manifestPath));
        Assert.NotEqual(Guid.Empty, summary.ProjectId);
    }

    [Fact]
    public async Task ManagedPathsAndUnsafeProjectPathsAreRejected()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
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
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
        StackFailureException mainError = await Assert.ThrowsAsync<StackFailureException>(() => engine.PruneDomainAsync(main, Admin));
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        var withoutTwoFactor = new StackActor("maintainer-no-2fa", Admin.Capabilities, TwoFactorVerified: false);
        StackFailureException twoFactorError = await Assert.ThrowsAsync<StackFailureException>(() => engine.PruneDomainAsync(branch.Id, withoutTwoFactor));

        Assert.Equal(StackFailureCode.InvalidHierarchy, mainError.Code);
        Assert.Equal(StackFailureCode.PermissionDenied, twoFactorError.Code);
    }

    [Fact]
    public async Task Pruning_the_active_branch_restores_main_as_the_single_active_domain()
    {
        var engine = await CreateProjectAsync(NewDirectory());
        StackDomainSnapshot main = await engine.OpenProjectAsync(Admin);
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Active feature", Admin);
        await engine.SetActiveDomainAsync(branch.Id, Admin);
        await engine.PruneDomainAsync(branch.Id, Admin);

        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        StackLineagePage lineage = await engine.GetLineagePageAsync(Admin, 0, 100);

        Assert.Equal(main.Id, project.ActiveDomainId);
        Assert.Equal(main.Id, Assert.Single(lineage.Entries, entry => entry.IsActive).Id);
        Assert.DoesNotContain(lineage.Entries, entry => entry.Id == branch.Id);
    }

    [Fact]
    public async Task Child_restore_waits_for_pruned_parent_then_restores_the_same_domain_identity()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
        StackProjectSummary project = await engine.GetProjectSummaryAsync(Admin);
        StackDomainSnapshot branch = await engine.CreateBranchAsync(project.ProjectId, "Branch", Admin);
        StackDomainSnapshot twig = await engine.CreateTwigAsync(branch.Id, "Twig", Admin);
        await engine.PruneDomainAsync(twig.Id, Admin);
        await engine.PruneDomainAsync(branch.Id, Admin);

        StackFailureException blocked = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.RestorePrunedDomainAsync(twig.Id, Admin));
        Assert.Equal(StackFailureCode.RestoreIncomplete, blocked.Code);
        Assert.Equal(branch.Id.ToString("D"), blocked.Target);
        Assert.True(blocked.Recoverable); Assert.True(blocked.Retryable);
        Assert.Equal(new[] { main }, (await engine.GetLineagePageAsync(Admin, 0, 100)).Entries.Select(entry => entry.Id));
        var beforeParentRestore = new StackEngine(new JsonFileStackProjectStore(directory));
        await beforeParentRestore.OpenProjectAsync(Admin);
        Assert.Equal(new[] { main }, (await beforeParentRestore.GetLineagePageAsync(Admin, 0, 100)).Entries.Select(entry => entry.Id));

        StackDomainSnapshot restoredBranch = await engine.RestorePrunedDomainAsync(branch.Id, Admin);
        StackDomainSnapshot restoredTwig = await engine.RestorePrunedDomainAsync(twig.Id, Admin);
        Assert.Equal(branch.Id, restoredBranch.Id);
        Assert.Equal(twig.Id, restoredTwig.Id);
        Assert.Equal(branch.Id, restoredTwig.ParentId);
        Assert.Equal(new[] { main, branch.Id, twig.Id }, (await engine.GetLineagePageAsync(Admin, 0, 100))
            .Entries.Select(entry => entry.Id));
        var reopened = new StackEngine(new JsonFileStackProjectStore(directory));
        await reopened.OpenProjectAsync(Admin);
        Assert.Equal(new[] { main, branch.Id, twig.Id }, (await reopened.GetLineagePageAsync(Admin, 0, 100))
            .Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task Stale_restore_refuses_after_external_revision_change_then_reopen_restores_same_domain()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Retained", Admin);
        await engine.PruneDomainAsync(branch.Id, Admin);

        var external = new StackEngine(new JsonFileStackProjectStore(directory));
        await external.OpenProjectAsync(Admin);
        await external.ApplyChangeAsync(main, Upsert("external.txt", "newer source revision"), Admin);
        string manifestPath = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] currentSource = await File.ReadAllBytesAsync(manifestPath);
        StackFailureException stale = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.RestorePrunedDomainAsync(branch.Id, Admin));

        Assert.Equal(StackFailureCode.RevisionConflict, stale.Code);
        Assert.Equal(currentSource, await File.ReadAllBytesAsync(manifestPath));
        JsonNode retained = JsonNode.Parse(currentSource)!;
        Assert.True(Domain(retained, branch.Id)["isDeleted"]!.GetValue<bool>());
        Assert.DoesNotContain(Domain(retained, main)["childDomainIds"]!.AsArray(),
            child => Guid.Parse(child!.GetValue<string>()) == branch.Id);
        Assert.DoesNotContain(retained["audit"]!.AsArray(), entry =>
            StringComparer.Ordinal.Equals(entry!["action"]!.GetValue<string>(), "DomainRestored") &&
            Guid.Parse(entry["targetId"]!.GetValue<string>()) == branch.Id);

        await engine.OpenProjectAsync(Admin);
        StackDomainSnapshot restored = await engine.RestorePrunedDomainAsync(branch.Id, Admin);
        Assert.Equal(branch.Id, restored.Id);
        JsonNode afterRestore = JsonNode.Parse(await File.ReadAllBytesAsync(manifestPath))!;
        Assert.False(Domain(afterRestore, branch.Id)["isDeleted"]!.GetValue<bool>());
        Assert.Contains(Domain(afterRestore, main)["childDomainIds"]!.AsArray(),
            child => Guid.Parse(child!.GetValue<string>()) == branch.Id);
        Assert.Single(afterRestore["audit"]!.AsArray(), entry =>
            StringComparer.Ordinal.Equals(entry!["action"]!.GetValue<string>(), "DomainRestored") &&
            Guid.Parse(entry["targetId"]!.GetValue<string>()) == branch.Id);
        Assert.Contains((await new StackEngine(new JsonFileStackProjectStore(directory)).GetLineagePageAsync(Admin, 0, 100)).Entries,
            entry => entry.Id == branch.Id);

        static JsonObject Domain(JsonNode source, Guid id) => source["domains"]!.AsArray()
            .Select(node => node!.AsObject()).Single(domain => Guid.Parse(domain["id"]!.GetValue<string>()) == id);
    }

    [Fact]
    public async Task Expired_retention_restore_refuses_without_changing_retained_manifest_or_parent_links()
    {
        string directory = NewDirectory();
        var engine = new StackEngine(new JsonFileStackProjectStore(directory));
        await engine.CreateProjectAsync(new StackProjectConfiguration("Expired restore", StackStorageMode.Files,
            directory, DeletedRetention: TimeSpan.Zero), Admin);
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Expired", Admin);
        await engine.PruneDomainAsync(branch.Id, Admin);
        string manifestPath = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] before = await File.ReadAllBytesAsync(manifestPath);

        StackFailureException expired = await Assert.ThrowsAsync<StackFailureException>(
            () => engine.RestorePrunedDomainAsync(branch.Id, Admin));

        Assert.Equal(StackFailureCode.RestoreIncomplete, expired.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(manifestPath));
        JsonNode retained = JsonNode.Parse(before)!;
        JsonObject deleted = retained["domains"]!.AsArray().Select(node => node!.AsObject())
            .Single(domain => Guid.Parse(domain["id"]!.GetValue<string>()) == branch.Id);
        Assert.True(deleted["isDeleted"]!.GetValue<bool>());
        JsonObject parent = retained["domains"]!.AsArray().Select(node => node!.AsObject())
            .Single(domain => Guid.Parse(domain["id"]!.GetValue<string>()) == main);
        Assert.DoesNotContain(parent["childDomainIds"]!.AsArray(), child =>
            Guid.Parse(child!.GetValue<string>()) == branch.Id);
        Assert.DoesNotContain(retained["audit"]!.AsArray(), entry =>
            StringComparer.Ordinal.Equals(entry!["action"]!.GetValue<string>(), "DomainRestored") &&
            Guid.Parse(entry["targetId"]!.GetValue<string>()) == branch.Id);
    }

    [Fact]
    public async Task Open_rejects_live_child_with_deleted_parent_without_rewriting_recovery_source()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Branch", Admin);
        StackDomainSnapshot twig = await engine.CreateTwigAsync(branch.Id, "Twig", Admin);
        string path = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var source = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        var domains = source["domains"]!.AsArray();
        var parent = domains.Select(node => node!.AsObject()).Single(domain => Guid.Parse(domain["id"]!.GetValue<string>()) == main);
        var deletedParent = domains.Select(node => node!.AsObject()).Single(domain => Guid.Parse(domain["id"]!.GetValue<string>()) == branch.Id);
        deletedParent["isDeleted"] = true; deletedParent["isActive"] = false;
        deletedParent["deletedAt"] = DateTimeOffset.UtcNow.ToString("O");
        var parentChildren = parent["childDomainIds"]!.AsArray();
        for (int index = parentChildren.Count - 1; index >= 0; index--)
            if (Guid.Parse(parentChildren[index]!.GetValue<string>()) == branch.Id) parentChildren.RemoveAt(index);
        await File.WriteAllTextAsync(path, source.ToJsonString());
        byte[] corruptSource = await File.ReadAllBytesAsync(path);

        StackFailureException refused = await Assert.ThrowsAsync<StackFailureException>(
            () => new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync(Admin));

        Assert.Equal(StackFailureCode.SourceCorrupt, refused.Code);
        Assert.Equal(twig.Id.ToString("D"), refused.Target);
        Assert.Equal(corruptSource, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Open_rejects_missing_parent_child_link_without_rewriting_canonical_manifest()
    {
        string directory = NewDirectory();
        var engine = await CreateProjectAsync(directory);
        Guid main = (await engine.OpenProjectAsync(Admin)).Id;
        Guid projectId = (await engine.GetProjectSummaryAsync(Admin)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Branch", Admin);
        string path = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var source = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        var mainDomain = source["domains"]!.AsArray().Select(node => node!.AsObject())
            .Single(domain => Guid.Parse(domain["id"]!.GetValue<string>()) == main);
        var children = mainDomain["childDomainIds"]!.AsArray();
        for (int index = children.Count - 1; index >= 0; index--)
            if (Guid.Parse(children[index]!.GetValue<string>()) == branch.Id) children.RemoveAt(index);
        await File.WriteAllTextAsync(path, source.ToJsonString());
        byte[] corruptSource = await File.ReadAllBytesAsync(path);

        StackFailureException refused = await Assert.ThrowsAsync<StackFailureException>(
            () => new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync(Admin));

        Assert.Equal(StackFailureCode.SourceCorrupt, refused.Code);
        Assert.Equal(branch.Id.ToString("D"), refused.Target);
        Assert.Equal(corruptSource, await File.ReadAllBytesAsync(path));
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
        Guid mainId = (await engine.OpenProjectAsync(Admin)).Id;
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
