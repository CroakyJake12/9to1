using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Apps.Stacks.NativeUI.Tests;

public sealed partial class StackNativeWorkspaceTests
{
    [Theory]
    [InlineData("StacksRejectConflict", StackConflictProposalState.Rejected, false, false)]
    [InlineData("StacksAcceptConflict", StackConflictProposalState.AcceptedWithoutMerge, true, false)]
    [InlineData("StacksMergeConflict", StackConflictProposalState.AcceptedAndMerged, true, true)]
    [InlineData("StacksAcceptConflict", StackConflictProposalState.PendingReview, false, false, true)]
    [InlineData("StacksRejectConflict", StackConflictProposalState.Rejected, false, false, false, true)]
    public async Task Review_buttons_send_the_exact_proposal_and_preserve_distinct_outcomes(string button,
        StackConflictProposalState expected, bool resolves, bool merged, bool stale = false, bool rootLocked = false)
    {
        var (engine, directory) = await ProjectAsync();
        var parent = await engine.OpenProjectAsync();
        Task Change(Guid domain, string value) =>
            engine.ApplyChangeAsync(domain, new(StackMutationKind.Upsert, "review.txt", new(Encoding.UTF8.GetBytes(value))), Actor);
        await Change(parent.Id, "base text");
        await engine.CreateCommitAsync(parent.Id, "Base", Actor);
        var branch = await engine.CreateDomainAsync(parent.Id, "Review branch", Actor);
        await Change(branch.Id, "local text");
        var branchHead = await engine.CreateCommitAsync(branch.Id, "Local", Actor);
        await Change(parent.Id, "incoming text");
        var parentHead = await engine.CreateCommitAsync(parent.Id, "Incoming", Actor);
        var conflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Actor));
        var proposed = await engine.ProposeConflictResolutionAsync(conflict.Id, "Choose both edits",
            new(Encoding.UTF8.GetBytes("combined text")), "Reviewer", Actor);
        if (rootLocked)
            await engine.CreateRootAsync(parent.Id, ["review.txt"], StackActor.System);

        await using var native = HeadlessUnitTestSession.StartNew(typeof(StackNativeTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var workspace = await WorkspaceAsync(engine);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(workspace); loader.SetActionDispatcher(workspace);
            var root = Assert.IsAssignableFrom<Control>(loader.Load(StackCuiWorkspace.LoadDocument()));
            loader.WireBindings(root);
            var window = new Window { Width = 1024, Height = 768, Content = root };
            window.Show();
            try
            {
                await OpenAsync(workspace);
                var branchRow = Assert.Single(Rows(workspace, "Domains"), row => row.Key == branch.Id.ToString("D"));
                await workspace.DispatchAsync("9to1.Stacks.SelectDomain", branchRow.Target);
                var list = Assert.Single(root.GetVisualDescendants().OfType<Button>(), b =>
                    b.Tag as string == "ShowConflict" && b.IsEffectivelyVisible);
                var manifest = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath);
                var before = await File.ReadAllBytesAsync(manifest);
                await ClickAsync(loader, workspace, list);
                Assert.Equal("review.txt", Value(workspace, "ConflictReviewPath"));
                Assert.Equal("base text", Value(workspace, "ConflictReviewBase"));
                Assert.Equal("local text", Value(workspace, "ConflictReviewLocal"));
                Assert.Equal("incoming text", Value(workspace, "ConflictReviewIncoming"));
                Assert.Equal("combined text", Value(workspace, "ConflictReviewProposed"));
                Assert.Contains(conflict.IncomingRevisionId.ToString("D"), Assert.IsType<string>(Value(workspace, "ConflictReviewRevisions")));
                Assert.Equal(before, await File.ReadAllBytesAsync(manifest));

                if (rootLocked)
                {
                    Assert.Contains("authoritative Root", Assert.IsType<string>(Value(workspace, "ConflictReviewRestriction")));
                    Assert.False(NamedButton(root, "StacksAcceptConflict").IsEnabled);
                    Assert.False(NamedButton(root, "StacksMergeConflict").IsEnabled);
                    Assert.Equal(StackFailureCode.RootLocked, (await Assert.ThrowsAsync<StackFailureException>(() =>
                        engine.ReviewConflictProposalAsync(proposed.Id, accept: true, merge: true, Actor))).Code);
                    Assert.Equal(before, await File.ReadAllBytesAsync(manifest));
                }

                if (stale)
                {
                    await Change(parent.Id, "new upstream content");
                    await engine.CreateCommitAsync(parent.Id, "Change after opening review", Actor);
                    before = await File.ReadAllBytesAsync(manifest);
                }

                await ClickAsync(loader, workspace, NamedButton(root, button));
                var actualProposal = Assert.Single(await engine.GetConflictProposalsAsync(Actor), p => p.Id == proposed.Id);
                Assert.Equal(expected, actualProposal.State);
                var actualConflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Actor));
                Assert.Equal(resolves, actualConflict.IsResolved);
                Assert.Equal(merged, actualConflict.MergeCompleted);
                var liveBranch = Assert.Single(await engine.GetLineageAsync(), d => d.Id == branch.Id);
                if (merged) Assert.NotEqual(branchHead.Id, liveBranch.HeadRevisionId);
                else Assert.Equal(branchHead.Id, liveBranch.HeadRevisionId);
                if (resolves && !merged) Assert.Contains(liveBranch.WorkingChanges, change => change.Path == "review.txt");
                if (stale)
                {
                    Assert.Contains("changed", Assert.IsType<string>(Value(workspace, "Status")));
                    Assert.Equal(before, await File.ReadAllBytesAsync(manifest));
                    Assert.True(workspace.IsActionAvailable("9to1.Stacks.Refresh"));
                }
                else
                {
                    var parentNow = Assert.Single(await engine.GetLineageAsync(), d => d.Id == parent.Id);
                    Assert.Equal(parentHead.Id, parentNow.HeadRevisionId);
                }
                return true;
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); await workspace.CloseOriginalAsync(); window.Close(); }
        }, CancellationToken.None));
    }
}
