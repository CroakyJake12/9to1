using System.Diagnostics;

using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Ucml_Ask_owner_requires_visible_exact_one_use_accept_and_refuses_rebinding() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var prepared = await rig.PrepareAsync(binding);
        var actor = (await rig.Profiles.GetCurrentAsync(Token))!;
        var owner = new HomeUcmlContextApprovalOwner(rig.OriginalStateStore, rig.Profiles, rig.Permissions, rig.Memory);
        var request = Guid.NewGuid(); var id = Guid.NewGuid(); var policy = new string('A', 64);
        Task<bool> Request(Guid requestId, Guid recordId, string policyHash) => owner.AuthorizeOriginalAsync(prepared.Input!, requestId,
            actor, binding.Definition.Identity.DefinitionId, binding.Conversation.Id, policyHash,
            [recordId], ["communication"], null, Scope, rig.Retain, Token);
        var approval = Request(request, id, policy);
        var pending = await ObserveUcmlApproval(rig, approval);
        Assert.True(pending.Policy.RequiresPerActionApproval);
        Assert.Contains(pending.Scope.Objects, value => value.ObjectType == "ucml-record" && value.ObjectId == id.ToString("D"));
        Assert.Contains(pending.Scope.Objects, value => value.ObjectType == "ucml-policy" && value.ObjectId == policy);
        Assert.False(approval.IsCompleted);
        Assert.True((await rig.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
        Assert.True(await approval);
        Assert.True(await Request(request, id, policy)); // Same logical dispatch guard, not a new request.
        var completed = await rig.Permissions.ReadRequestObservationAsync(pending.RequestId, Token);
        Assert.Equal(HomePermissionRequestState.Succeeded, completed!.State);
        Assert.Equal("HOME_UCML_SELECTION_ACCEPTED", completed.ResultCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Request(request, Guid.NewGuid(), policy));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Request(request, id, new string('B', 64)));
        var next = Request(Guid.NewGuid(), id, policy);
        var nextPending = await ObserveUcmlApproval(rig, next);
        Assert.NotEqual(pending.RequestId, nextPending.RequestId);
        Assert.True((await rig.Permissions.DecideAsync(nextPending.RequestId, HomeApprovalChoice.Decline, cancellationToken: Token)).Succeeded);
        Assert.False(await next);
        var snapshot = await rig.Permissions.GetSnapshotAsync(cancellationToken: Token);
        Assert.DoesNotContain(snapshot.Grants, grant => grant.Scope.ActionName == HomeUcmlContextApprovalOwner.Action);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Ucml_Ask_owner_expiry_or_cancellation_withdraws_pending_request_without_acceptance(bool cancel) => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var prepared = await rig.PrepareAsync(binding);
        var actor = (await rig.Profiles.GetCurrentAsync(Token))!;
        var owner = new HomeUcmlContextApprovalOwner(rig.OriginalStateStore, rig.Profiles, rig.Permissions, rig.Memory);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var approval = owner.AuthorizeOriginalAsync(prepared.Input!, Guid.NewGuid(), actor,
            binding.Definition.Identity.DefinitionId, binding.Conversation.Id, new string('C', 64),
            [Guid.NewGuid()], ["communication"], cancel ? null : DateTimeOffset.UtcNow.AddSeconds(2),
            Scope, rig.Retain, cancellation.Token);
        var pending = await ObserveUcmlApproval(rig, approval);
        if (cancel)
        {
            cancellation.Cancel();
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => approval);
            Assert.True(ContainsCancellation(failure), "The original cancellation must remain visible after its exact withdrawal.");
        }
        else Assert.False(await approval.WaitAsync(TimeSpan.FromSeconds(8), Token));
        var stored = await rig.Permissions.ReadRequestObservationAsync(pending.RequestId, Token);
        Assert.Equal(HomePermissionRequestState.Cancelled, stored!.State);
        Assert.Equal("HOME_UCML_SELECTION_WITHDRAWN", stored.ResultCode);
        Assert.False((await rig.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
        Assert.DoesNotContain((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).RecentAuditEvents,
            value => value.RequestId == pending.RequestId && value.ResultCode == "HOME_UCML_SELECTION_ACCEPTED");
    });

    private static bool ContainsCancellation(Exception failure) => failure is OperationCanceledException ||
        failure is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsCancellation);

    private static async Task<HomePermissionRequest> ObserveUcmlApproval(Rig rig, Task actual)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            var pending = (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests
                .Where(value => value.Scope.ActionName == HomeUcmlContextApprovalOwner.Action).ToArray();
            if (pending.Length != 0) return Assert.Single(pending);
            if (actual.IsCompleted) { await actual; throw new InvalidOperationException("UCML completed without a real Home review."); }
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The actual Home UCML context review was not observed.");
    }
}
