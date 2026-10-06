using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class CloudflareProductionSetupTests
{
    [Fact] public Task Commit_without_prior_submit_publishes_the_actual_pending_review() => Run(async rig =>
    {
        Exception? primary = null; CloudflareSetupRequiredException? expected = null;
        try
        {
            var review = await rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, CancellationToken.None));
            var actual = rig.Own(review.CommitOriginalAsync(CancellationToken.None));
            expected = await Assert.ThrowsAsync<CloudflareSetupRequiredException>(() => actual); rig.Expect(actual);
            Assert.Equal("CF_SETUP_APPROVAL_REQUIRED", expected.Code);
            Assert.Equal(review.RequestId, expected.ReviewRequestId);
            Assert.Same(actual, review.CommitOriginalAsync(CancellationToken.None));
            var submit = rig.Own(review.SubmitOriginalAsync(CancellationToken.None));
            Assert.Same(submit, review.SubmitOriginalAsync(CancellationToken.None));
            Assert.Equal(review.RequestId, (await submit).ReviewRequestId);
            Assert.Equal(HomePermissionRequestState.PendingApproval,
                (await rig.Own(rig.Permissions.GetAuthorizationAsync(review.RequestId, CancellationToken.None))).State);
            Assert.DoesNotContain((await rig.State()).Records, row => row.RecordId == "home.cloudflare.connection");
            Assert.Equal(0, rig.Mcp.Calls);
        }
        catch (Exception cause) { primary = cause; }
        Task? close = null; var errors = new List<Exception>();
        try { close = rig.Own(rig.Owner.CloseAndDrainOriginalHostAsync()); }
        catch (Exception cause) { errors.Add(cause); }
        if (close is not null)
            try { await close; }
            catch (Exception cause)
            {
                var actual = close.Exception ?? cause;
                var leaves = SetupSubmitLeaves(actual).ToArray();
                if (expected is not null && leaves.Length != 0 && leaves.All(leaf => ReferenceEquals(leaf, expected))) rig.Expect(close);
                else errors.Add(actual);
            }
        if (primary is not null && errors.Count == 0) ExceptionDispatchInfo.Capture(primary).Throw();
        if (primary is not null) errors.Insert(0, primary);
        if (errors.Count != 0) throw new AggregateException("Actual setup commit control and owner cleanup failed.", errors);
    });
    private static IEnumerable<Exception> SetupSubmitLeaves(Exception cause)
    {
        if (cause is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var child in group.InnerExceptions) foreach (var leaf in SetupSubmitLeaves(child)) yield return leaf; }
        else yield return cause;
    }
}
