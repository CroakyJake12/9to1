using System.Text.Json;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core;
using Haven.Core.Shelf;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class ShelfMapsFinalClaimFenceTests
{
    [Theory]
    [InlineData(false, "blocked")] [InlineData(true, "blocked")]
    [InlineData(false, "terminal")] [InlineData(true, "terminal")]
    [InlineData(false, "binding")] [InlineData(true, "binding")]
    public async Task Actual_physical_settings_held_before_final_Home_acquisition_denies_late_action_or_original_receipt_change(bool maps, string change)
    {
        using var f = new Fixture(maps); await f.InitializeAsync();
        var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        var before = await File.ReadAllBytesAsync(f.SettingsFile);
        f.Settings.HoldAdmission = true;
        var commit = f.CommitAsync(review);
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(commit.IsCompleted); Assert.Equal(HomePermissionRequestState.Executing,
                (await f.Permissions.ReadRequestObservationAsync(f.RequestID(review)))!.State);
            if (change == "blocked") Assert.True((await f.Permissions.BlockCallerAsync(f.Actor.ActorId)).Succeeded);
            else if (change == "terminal") Assert.True((await f.Permissions.RecordExecutionAsync(f.RequestID(review),
                new(HomePermissionRequestState.Cancelled, "ExternalTerminal", "Actual permission terminal transition before final owner acquisition.", []))).Succeeded);
            else
            {
                var state = (await f.Home.ReadAsync()).State!;
                var record = Assert.Single(state.Records, item => item.RecordType == "home.local-store-ownership");
                var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await f.Home.WriteAsync(record with { Revision = record.Revision + 1,
                    Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "changed-original-receipt" }) }, record.Revision)).IsSuccess);
            }
        }
        finally { f.Settings.Release.TrySetResult(); }
        var result = await commit.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Committed); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(1, f.Settings.GuardedWriteCalls);
        await f.FinishAsync(review); Assert.Equal(1, f.Settings.GuardedWriteCalls);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Genuine_Settings_first_Home_lease_holds_late_block_until_physical_owner_commit_then_preserves_known_receipt(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.HoldPublication = true;
        var commit = f.CommitAsync(review); Task<HomePermissionOperationResult>? blocked = null;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            blocked = f.Permissions.BlockCallerAsync(f.Actor.ActorId);
            Assert.False(blocked.IsCompleted); Assert.False(commit.IsCompleted);
        }
        finally { f.Settings.Release.TrySetResult(); }
        var result = await commit.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Committed); Assert.True((await blocked!.WaitAsync(TimeSpan.FromSeconds(10))).Succeeded);
        await f.AssertOneCommittedAsync(); var bytes = await File.ReadAllBytesAsync(f.SettingsFile);
        var finish = await f.FinishAsync(review); Assert.True(finish.Committed);
        Assert.Equal(1, f.Settings.GuardedWriteCalls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_commit_return_loss_and_hidden_receipt_stays_unknown_until_original_evidence_without_Home_reentry_deadlock(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.LoseWriteReturnOnce = true; f.Settings.HideAfterLostReturn = true;
        var lost = await f.CommitAsync(review).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(lost.CompletionUnknown); Assert.False(lost.Committed); Assert.False(lost.AuditRecorded);
        var bytes = await File.ReadAllBytesAsync(f.SettingsFile); var home = await File.ReadAllBytesAsync(f.HomeFile);
        var hidden = await f.FinishAsync(review); Assert.True(hidden.CompletionUnknown);
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
        f.Settings.HideLibraryReads = false;
        var recovered = await f.FinishAsync(review).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(recovered.Committed); Assert.True(recovered.AuditRecorded);
        Assert.Equal(1, f.Settings.GuardedWriteCalls); Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
        await f.AssertOneCommittedAsync(); var terminal = await File.ReadAllBytesAsync(f.HomeFile);
        Assert.True((await f.FinishAsync(review)).Committed);
        Assert.Equal(terminal, await File.ReadAllBytesAsync(f.HomeFile)); Assert.Equal(1, f.Settings.GuardedWriteCalls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_actual_final_fence_composition_denies_without_owner_write_or_replay(bool maps)
    {
        using var f = new Fixture(maps, includeFence: false); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        var before = await File.ReadAllBytesAsync(f.SettingsFile);
        var home = await File.ReadAllBytesAsync(f.HomeFile);
        Assert.False((await f.CommitAsync(review)).Committed);
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
        Assert.Equal(HomePermissionRequestState.Approved, (await f.Permissions.ReadRequestObservationAsync(f.RequestID(review)))!.State);
        Assert.Equal(0, f.Settings.GuardedWriteCalls); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.False((await f.FinishAsync(review)).Committed); Assert.Equal(0, f.Settings.GuardedWriteCalls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_owner_library_revision_change_before_settings_transaction_preserves_competing_root_without_commit(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync(); var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.HoldBeforeWrite = true;
        var commit = f.CommitAsync(review); byte[] competing;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await f.CompeteAsync(); competing = await File.ReadAllBytesAsync(f.SettingsFile);
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.False((await commit.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(1, f.Settings.GuardedWriteCalls);
        Assert.False((await f.FinishAsync(review)).Committed);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_original_capability_competing_completion_waits_for_physical_owner_publication_and_cannot_erase_receipt(bool maps)
    {
        using var f = new Fixture(maps); await f.InitializeAsync();
        var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        f.Settings.HoldPublication = true;
        var commit = f.CommitAsync(review);
        Task<HomePermissionOperationResult>? completing = null;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Adversarial holder of the genuine original opaque handle; no minted or donor authority.
            // Test-only reflection leaves the production review API private.
            var property = review.GetType().GetProperty("Capability");
            Assert.NotNull(property);
            var actual = Assert.IsType<HomeResourceExecutionCapability>(property!.GetValue(review));
            completing = f.Broker.CompleteExecutionAsync(actual,
                new(HomePermissionRequestState.Cancelled, "CompetingOriginalCompletion", "Original handle completion races actual publication.", []));
            Assert.False(completing.IsCompleted); Assert.False(commit.IsCompleted);
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.True((await commit.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        await completing!.WaitAsync(TimeSpan.FromSeconds(10));
        await f.AssertOneCommittedAsync();
        var bytes = await File.ReadAllBytesAsync(f.SettingsFile);
        Assert.True((await f.FinishAsync(review)).Committed);
        Assert.Equal(1, f.Settings.GuardedWriteCalls);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Genuine_original_collection_review_commits_once_and_recovers_only_exact_physical_receipt(bool membership, bool lostReturn)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewCollectionAsync(membership);
        var before = await File.ReadAllBytesAsync(f.SettingsFile);
        Assert.Equal(HomePermissionRequestState.PendingApproval, (await f.Permissions.ReadRequestObservationAsync(f.RequestID(review)))!.State);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        await f.ApproveAsync(review);
        var writes = f.Settings.GuardedWriteCalls;
        f.Settings.LoseWriteReturnOnce = lostReturn; f.Settings.HideAfterLostReturn = lostReturn;
        var result = await f.CommitAsync(review);
        if (lostReturn)
        {
            Assert.False(result.Committed); Assert.True(result.CompletionUnknown);
            f.Settings.HideLibraryReads = false;
            result = await f.FinishAsync(review);
        }
        Assert.True(result.Committed); Assert.True(result.AuditRecorded); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        await f.AssertCollectionCommittedAsync(membership);
        var committed = await File.ReadAllBytesAsync(f.SettingsFile);
        var repeated = await f.FinishAsync(review); Assert.True(repeated.Committed); Assert.True(repeated.AuditRecorded);
        Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false, "blocked")] [InlineData(true, "blocked")]
    [InlineData(false, "terminal")] [InlineData(true, "terminal")]
    [InlineData(false, "binding")] [InlineData(true, "binding")]
    public async Task Genuine_collection_final_claim_denies_actual_late_Home_change_without_publication_or_replay(bool membership, string change)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewCollectionAsync(membership); await f.ApproveAsync(review);
        var before = await File.ReadAllBytesAsync(f.SettingsFile); var writes = f.Settings.GuardedWriteCalls;
        f.Settings.HoldAdmission = true; var pending = f.CommitAsync(review);
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(pending.IsCompleted);
            if (change == "blocked") Assert.True((await f.Permissions.BlockCallerAsync(f.Actor.ActorId)).Succeeded);
            else if (change == "terminal") Assert.True((await f.Permissions.RecordExecutionAsync(f.RequestID(review),
                new(HomePermissionRequestState.Cancelled, "CollectionTerminal", "Actual terminal transition before final acquisition.", []))).Succeeded);
            else
            {
                var state = (await f.Home.ReadAsync()).State!;
                var record = Assert.Single(state.Records, item => item.RecordType == "home.local-store-ownership");
                var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await f.Home.WriteAsync(record with { Revision = record.Revision + 1,
                    Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "collection-original-receipt-retired" }) }, record.Revision)).IsSuccess);
            }
        }
        catch
        {
            f.Settings.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch { /* Preserve the original observation failure. */ }
            throw;
        }
        finally { f.Settings.Release.TrySetResult(); }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Committed); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        await f.FinishAsync(review); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_collection_original_display_retirement_denies_before_claim_and_preserves_both_stores(bool membership)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewCollectionAsync(membership); await f.ApproveAsync(review);
        var home = await File.ReadAllBytesAsync(f.HomeFile); var settings = await File.ReadAllBytesAsync(f.SettingsFile);
        var writes = f.Settings.GuardedWriteCalls;
        // Adversarial holder retires the exact genuine privately issued display; no token constructed or copied.
        var property = review.GetType().GetProperty("Selection"); Assert.NotNull(property);
        var original = Assert.IsAssignableFrom<IShelfLibraryDisplay>(property!.GetValue(review)); original.Dispose();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.CommitAsync(review));
        Assert.Equal(writes, f.Settings.GuardedWriteCalls);
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile)); Assert.Equal(settings, await File.ReadAllBytesAsync(f.SettingsFile));
        var finish = await f.FinishAsync(review); Assert.False(finish.Committed);
        Assert.Equal(writes, f.Settings.GuardedWriteCalls);
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile)); Assert.Equal(settings, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_collection_competing_independent_store_revision_preserves_competitor_without_original_commit(bool membership)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewCollectionAsync(membership); await f.ApproveAsync(review);
        var writes = f.Settings.GuardedWriteCalls; f.Settings.HoldBeforeWrite = true;
        var pending = f.CommitAsync(review); byte[] competing = [];
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
            await f.CompeteCollectionAsync(); competing = await File.ReadAllBytesAsync(f.SettingsFile);
        }
        catch
        {
            f.Settings.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch { /* Preserve the original observation failure. */ }
            throw;
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        await f.FinishAsync(review); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Genuine_collection_original_completion_waits_for_actual_publication_and_cannot_erase_known_receipt(bool membership)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewCollectionAsync(membership); await f.ApproveAsync(review);
        var writes = f.Settings.GuardedWriteCalls; f.Settings.HoldPublication = true;
        var pending = f.CommitAsync(review); Task<HomePermissionOperationResult>? completion = null;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var property = review.GetType().GetProperty("Capability"); Assert.NotNull(property);
            var original = Assert.IsType<HomeResourceExecutionCapability>(property!.GetValue(review));
            completion = f.Broker.CompleteExecutionAsync(original,
                new(HomePermissionRequestState.Cancelled, "CompetingCollectionCompletion", "Actual original handle competes during held physical publication.", []));
            Assert.False(completion.IsCompleted); Assert.False(pending.IsCompleted);
        }
        catch
        {
            f.Settings.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            if (completion is not null) { try { await completion.WaitAsync(TimeSpan.FromSeconds(10)); } catch { } }
            throw;
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        await completion!.WaitAsync(TimeSpan.FromSeconds(10)); await f.AssertCollectionCommittedAsync(membership);
        var committed = await File.ReadAllBytesAsync(f.SettingsFile);
        Assert.True((await f.FinishAsync(review)).Committed); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData(false, "arguments")] [InlineData(true, "arguments")]
    [InlineData(false, "action")] [InlineData(true, "action")]
    [InlineData(false, "foreignHome")] [InlineData(true, "foreignHome")]
    public async Task Genuine_claimed_collection_capture_refuses_substitution_or_foreign_composition_without_consuming_original_effect(bool membership, string attack)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewCollectionAsync(membership); await f.ApproveAsync(review);
        var writes = f.Settings.GuardedWriteCalls; f.Settings.HoldAdmission = true; var pending = f.CommitAsync(review);
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var capabilityProperty = review.GetType().GetProperty("Capability"); Assert.NotNull(capabilityProperty);
            var original = Assert.IsType<HomeResourceExecutionCapability>(capabilityProperty!.GetValue(review));
            var argsProperty = review.GetType().GetProperty("Arguments"); Assert.NotNull(argsProperty);
            var args = Assert.IsType<JsonElement>(argsProperty!.GetValue(review));
            var action = membership ? HomeShelfLibraryOwner.MembershipActionID : HomeShelfLibraryOwner.CollectionActionID;
            var store = f.Home;
            var foreignPath = Path.Combine(f.Paths.DataDirectory, "foreign-home.json");
            if (attack == "arguments")
            {
                var altered = System.Text.Json.Nodes.JsonNode.Parse(args.GetRawText())!;
                altered["operationID"] = Guid.NewGuid(); args = JsonSerializer.SerializeToElement(altered);
            }
            else if (attack == "action") action = HomeShelfLibraryOwner.ActionID;
            else store = new FileHomeCoreStateStore(foreignPath);
            var home = await File.ReadAllBytesAsync(f.HomeFile); var settings = await File.ReadAllBytesAsync(f.SettingsFile);
            var refused = await HomeClaimedResourceCommitFence.CaptureShelfCollectionAsync(f.Broker, store, f.Profiles, f.Authority,
                args.GetProperty("storeID").GetGuid().ToString("D"), args.GetProperty("revision").GetInt64(), action,
                args, original, f.Actor, () => true);
            Assert.Null(refused); Assert.False(File.Exists(foreignPath));
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile)); Assert.Equal(settings, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.False(pending.IsCompleted);
        }
        catch
        {
            f.Settings.Release.TrySetResult(); try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            throw;
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls); await f.AssertCollectionCommittedAsync(membership);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Genuine_item_edit_approval_original_receipt_and_lost_return_recovery_never_replays(bool loseReturn, bool hideReceipt)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewItemEditAsync(["work", "reference"]);
        var original = await File.ReadAllBytesAsync(f.SettingsFile); var writes = f.Settings.GuardedWriteCalls;
        Assert.False((await f.CommitAsync(review)).Committed);
        Assert.Equal(original, await File.ReadAllBytesAsync(f.SettingsFile)); Assert.Equal(writes, f.Settings.GuardedWriteCalls);
        await f.ApproveAsync(review);
        f.Settings.LoseWriteReturnOnce = loseReturn; f.Settings.HideAfterLostReturn = hideReceipt;
        var observed = await f.CommitAsync(review);
        if (hideReceipt)
        {
            Assert.False(observed.Committed); Assert.True(observed.CompletionUnknown); Assert.False(observed.AuditRecorded);
            await f.AssertItemEditCommittedAsync();
            Assert.True((await f.FinishAsync(review)).CompletionUnknown);
            f.Settings.HideLibraryReads = false; observed = await f.FinishAsync(review);
        }
        Assert.True(observed.Committed); Assert.True(observed.AuditRecorded);
        await f.AssertItemEditCommittedAsync(); var committed = await File.ReadAllBytesAsync(f.SettingsFile);
        var finish = await f.FinishAsync(review); Assert.True(finish.Committed); Assert.True(finish.AuditRecorded);
        Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls); Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
    }
    [Theory]
    [InlineData("blocked")] [InlineData("terminal")] [InlineData("binding")]
    public async Task Genuine_item_edit_final_original_Home_change_denies_physical_publication(string change)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewItemEditAsync(["work", "reference"]); await f.ApproveAsync(review);
        var before = await File.ReadAllBytesAsync(f.SettingsFile); var writes = f.Settings.GuardedWriteCalls;
        f.Settings.HoldAdmission = true; var pending = f.CommitAsync(review);
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
            if (change == "blocked") Assert.True((await f.Permissions.BlockCallerAsync(f.Actor.ActorId)).Succeeded);
            else if (change == "terminal") Assert.True((await f.Permissions.RecordExecutionAsync(f.RequestID(review),
                new(HomePermissionRequestState.Cancelled, "ExternalTerminal", "Actual late item-edit request transition.", []))).Succeeded);
            else
            {
                var record = Assert.Single((await f.Home.ReadAsync()).State!.Records, x => x.RecordType == "home.local-store-ownership");
                var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await f.Home.WriteAsync(record with { Revision = record.Revision + 1,
                    Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "changed-original-edit-receipt" }) }, record.Revision)).IsSuccess);
            }
        }
        catch { f.Settings.Release.TrySetResult(); try { await pending; } catch { } throw; }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        await f.FinishAsync(review); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
    }
    [Fact]
    public async Task Original_item_edit_tags_are_frozen_before_real_identity_return_wait()
    {
        using var f = new Fixture(false); await f.InitializeAsync(); var tags = new[] { "work", "reference" };
        var pending = f.ReviewItemEditAsync(tags, true); object? review = null;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
            tags[0] = "replacement"; f.Settings.Release.TrySetResult(); review = await pending;
            await f.ApproveAsync(review); var committed = await f.CommitAsync(review);
            Assert.True(committed.Committed); Assert.True(committed.AuditRecorded); await f.AssertItemEditCommittedAsync();
        }
        catch { f.Settings.Release.TrySetResult(); try { await pending; } catch { } throw; }
        finally { f.Settings.Release.TrySetResult(); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Genuine_smart_collection_freezes_local_criteria_and_retains_canonical_match_after_reopen(bool holdIdentity)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var tags = new[] { "work" }; var kinds = new[] { ShelfTargetKind.InstalledApplication };
        var pending = f.ReviewSmartCollectionAsync(tags, kinds, holdIdentity); object? review = null;
        try
        {
            if (holdIdentity)
            {
                await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
                tags[0] = "replacement"; kinds[0] = ShelfTargetKind.WebAddress; f.Settings.Release.TrySetResult();
            }
            review = await pending;
            var before = await File.ReadAllBytesAsync(f.SettingsFile); var writes = f.Settings.GuardedWriteCalls;
            Assert.False((await f.CommitAsync(review)).Committed); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.Equal(writes, f.Settings.GuardedWriteCalls);
            await f.ApproveAsync(review); var committed = await f.CommitAsync(review);
            Assert.True(committed.Committed); Assert.True(committed.AuditRecorded); await f.AssertSmartCollectionCommittedAsync();
            var bytes = await File.ReadAllBytesAsync(f.SettingsFile);
            Assert.True((await f.FinishAsync(review)).Committed); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        catch { f.Settings.Release.TrySetResult(); try { await pending; } catch { } throw; }
        finally { f.Settings.Release.TrySetResult(); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_smart_collection_known_or_hidden_lost_return_recovers_only_original_receipt(bool hideReceipt)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewSmartCollectionAsync(["work"], [ShelfTargetKind.InstalledApplication]);
        await f.ApproveAsync(review); var writes = f.Settings.GuardedWriteCalls;
        f.Settings.LoseWriteReturnOnce = true; f.Settings.HideAfterLostReturn = hideReceipt;
        var result = await f.CommitAsync(review);
        if (hideReceipt)
        {
            Assert.True(result.CompletionUnknown); Assert.False(result.Committed); Assert.False(result.AuditRecorded);
            await f.AssertSmartCollectionCommittedAsync(); Assert.True((await f.FinishAsync(review)).CompletionUnknown);
            f.Settings.HideLibraryReads = false; result = await f.FinishAsync(review);
        }
        Assert.True(result.Committed); Assert.True(result.AuditRecorded); await f.AssertSmartCollectionCommittedAsync();
        var bytes = await File.ReadAllBytesAsync(f.SettingsFile);
        Assert.True((await f.FinishAsync(review)).Committed); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.SettingsFile));
    }
    [Theory]
    [InlineData("blocked")] [InlineData("terminal")] [InlineData("binding")]
    public async Task Genuine_smart_collection_final_original_Home_change_denies_physical_publication(string change)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewSmartCollectionAsync(["work"], [ShelfTargetKind.InstalledApplication]); await f.ApproveAsync(review);
        var before = await File.ReadAllBytesAsync(f.SettingsFile); var writes = f.Settings.GuardedWriteCalls;
        f.Settings.HoldAdmission = true; var pending = f.CommitAsync(review);
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
            if (change == "blocked") Assert.True((await f.Permissions.BlockCallerAsync(f.Actor.ActorId)).Succeeded);
            else if (change == "terminal") Assert.True((await f.Permissions.RecordExecutionAsync(f.RequestID(review),
                new(HomePermissionRequestState.Cancelled, "ExternalTerminal", "Actual late smart-collection request transition.", []))).Succeeded);
            else
            {
                var record = Assert.Single((await f.Home.ReadAsync()).State!.Records, x => x.RecordType == "home.local-store-ownership");
                var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await f.Home.WriteAsync(record with { Revision = record.Revision + 1,
                    Payload = JsonSerializer.SerializeToElement(binding with { ObservedStoreRevision = "changed-original-smart-receipt" }) }, record.Revision)).IsSuccess);
            }
        }
        catch { f.Settings.Release.TrySetResult(); try { await pending; } catch { } throw; }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        await f.FinishAsync(review); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
    }
    [Fact]
    public async Task Genuine_item_edit_original_completion_waits_for_actual_publication_and_cannot_erase_known_receipt()
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewItemEditAsync(["work", "reference"]); await f.ApproveAsync(review);
        var writes = f.Settings.GuardedWriteCalls; f.Settings.HoldPublication = true;
        var pending = f.CommitAsync(review); Task<HomePermissionOperationResult>? completion = null;
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var property = review.GetType().GetProperty("Capability"); Assert.NotNull(property);
            var original = Assert.IsType<HomeResourceExecutionCapability>(property!.GetValue(review));
            completion = f.Broker.CompleteExecutionAsync(original,
                new(HomePermissionRequestState.Cancelled, "CompetingItemEditCompletion", "Actual original handle competes during held physical publication.", []));
            Assert.False(completion.IsCompleted); Assert.False(pending.IsCompleted);
        }
        catch
        {
            f.Settings.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            if (completion is not null) { try { await completion.WaitAsync(TimeSpan.FromSeconds(10)); } catch { } }
            throw;
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        await completion!.WaitAsync(TimeSpan.FromSeconds(10)); await f.AssertItemEditCommittedAsync();
        var committed = await File.ReadAllBytesAsync(f.SettingsFile);
        Assert.True((await f.FinishAsync(review)).Committed); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
    }

    [Theory]
    [InlineData("arguments")] [InlineData("action")] [InlineData("foreignHome")]
    public async Task Genuine_claimed_item_edit_capture_refuses_substitution_or_foreign_composition_without_consuming_original_effect(string attack)
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewItemEditAsync(["work", "reference"]); await f.ApproveAsync(review);
        var writes = f.Settings.GuardedWriteCalls; f.Settings.HoldAdmission = true; var pending = f.CommitAsync(review);
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var capabilityProperty = review.GetType().GetProperty("Capability"); Assert.NotNull(capabilityProperty);
            var original = Assert.IsType<HomeResourceExecutionCapability>(capabilityProperty!.GetValue(review));
            var argsProperty = review.GetType().GetProperty("Arguments"); Assert.NotNull(argsProperty);
            var args = Assert.IsType<JsonElement>(argsProperty!.GetValue(review));
            var action = HomeShelfLibraryOwner.ItemEditActionID;
            var store = f.Home;
            var foreignPath = Path.Combine(f.Paths.DataDirectory, "foreign-home.json");
            if (attack == "arguments")
            {
                var altered = System.Text.Json.Nodes.JsonNode.Parse(args.GetRawText())!;
                altered["operationID"] = Guid.NewGuid(); args = JsonSerializer.SerializeToElement(altered);
            }
            else if (attack == "action") action = HomeShelfLibraryOwner.ActionID;
            else store = new FileHomeCoreStateStore(foreignPath);
            var home = await File.ReadAllBytesAsync(f.HomeFile); var settings = await File.ReadAllBytesAsync(f.SettingsFile);
            var refused = await HomeClaimedResourceCommitFence.CaptureShelfItemEditAsync(f.Broker, store, f.Profiles, f.Authority,
                args.GetProperty("storeID").GetGuid().ToString("D"), args.GetProperty("revision").GetInt64(), action,
                args, original, f.Actor, () => true);
            Assert.Null(refused); Assert.False(File.Exists(foreignPath));
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile)); Assert.Equal(settings, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.False(pending.IsCompleted);
        }
        catch
        {
            f.Settings.Release.TrySetResult(); try { await pending.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            throw;
        }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls); await f.AssertItemEditCommittedAsync();
    }

    [Fact]
    public async Task Actual_item_edit_independent_same_store_revision_preserves_competitor_and_never_replays()
    {
        using var f = new Fixture(false); await f.InitializeAsync();
        var review = await f.ReviewItemEditAsync(["work", "reference"]); await f.ApproveAsync(review);
        var writes = f.Settings.GuardedWriteCalls; f.Settings.HoldBeforeWrite = true;
        var pending = f.CommitAsync(review); byte[] competing = [];
        try
        {
            await f.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
            await f.CompeteCollectionAsync(); competing = await File.ReadAllBytesAsync(f.SettingsFile);
        }
        catch { f.Settings.Release.TrySetResult(); try { await pending; } catch { } throw; }
        finally { f.Settings.Release.TrySetResult(); }
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Committed);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
        await f.FinishAsync(review); Assert.Equal(writes + 1, f.Settings.GuardedWriteCalls);
        Assert.Equal(competing, await File.ReadAllBytesAsync(f.SettingsFile));
    }
    [Theory]
    [InlineData(false, "rawHome")] [InlineData(true, "rawHome")]
    [InlineData(false, "profiles")] [InlineData(true, "profiles")]
    public async Task Genuine_approved_original_request_rejects_misbound_raw_source_before_consumption_or_publication(bool maps, string sourceMode)
    {
        using var f = new Fixture(maps, sourceMode: sourceMode); await f.InitializeAsync();
        var review = await f.ReviewAsync(); await f.ApproveAsync(review);
        var settings = await File.ReadAllBytesAsync(f.SettingsFile);
        var home = await File.ReadAllBytesAsync(f.HomeFile);
        Assert.False(File.Exists(f.ForeignHomeFile));
        Assert.False((await f.CommitAsync(review)).Committed);
        Assert.Equal(HomePermissionRequestState.Approved,
            (await f.Permissions.ReadRequestObservationAsync(f.RequestID(review)))!.State);
        Assert.Equal(0, f.Settings.GuardedWriteCalls);
        Assert.Equal(settings, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
        Assert.False(File.Exists(f.ForeignHomeFile));
        Assert.False((await f.FinishAsync(review)).Committed);
        Assert.False((await f.CommitAsync(review)).Committed);
        Assert.Equal(HomePermissionRequestState.Approved,
            (await f.Permissions.ReadRequestObservationAsync(f.RequestID(review)))!.State);
        Assert.Equal(0, f.Settings.GuardedWriteCalls);
        Assert.Equal(settings, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
        Assert.False(File.Exists(f.ForeignHomeFile));
    }

    private sealed class Fixture(bool maps, bool includeFence = true, string? sourceMode = null) : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public string HomeFile => Path.Combine(Paths.DataDirectory, "home.json");
        public string ForeignHomeFile => Path.Combine(Paths.DataDirectory, "foreign-home.json");
        public HeldSettings Settings { get; private set; } = null!;
        public FileHomeCoreStateStore Home { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public HomeResourceOperationBroker Broker { get; private set; } = null!;
        public HomeLocalProfileIdentity Profiles { get; private set; } = null!;
        public HomeResourceStoreOwnershipAuthority Authority { get; private set; } = null!;
        private HomeMapsLibraryOwner? _maps; private HomeShelfLibraryOwner? _shelf;
        private MapsJourneyService _journeys = null!; private ShelfLibraryService _library = null!;
        public async Task InitializeAsync()
        {
            Settings = new(new VersionedAtomicSettingsStore(Paths)); Home = new(HomeFile);
            var profiles = Profiles = new HomeLocalProfileIdentity(Home, new OperatingSystemPrincipalSource());
            Actor = (await profiles.GetCurrentAsync(default))!;
            var mapPolicy = new MapsOwnedLibraryActionPolicies(); var shelfPolicy = new ShelfOwnedLibraryActionPolicies();
            Permissions = new(Home, (app, action) => mapPolicy.TryGet(app, action) ?? shelfPolicy.TryGet(app, action));
            _journeys = new(Settings); _library = new(Settings);
            var evidence = maps ? (IHomeLocalStoreEvidenceProvider)new MapsOwnedLibraryEvidence(Settings) : new ShelfOwnedLibraryEvidence(Settings);
            var ownership = new HomeLocalStoreOwnership(Home, profiles, new HomeLocalStoreEvidenceRegistry([evidence]), Permissions);
            var authority = Authority = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
            ICanonicalResourceAccessResolver resolver = maps ? new MapsOwnedLibraryAccessResolver(_journeys, profiles, authority)
                : new ShelfOwnedLibraryAccessResolver(_library, profiles, authority);
            var broker = Broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(profiles, [resolver]), Permissions);
            var sourceHome = sourceMode == "rawHome" ? new FileHomeCoreStateStore(ForeignHomeFile) : Home;
            var sourceProfiles = sourceMode == "profiles" ? new HomeLocalProfileIdentity(Home, new OperatingSystemPrincipalSource()) : profiles;
            var source = includeFence ? new HomeOwnedLibraryCommitFenceSource(sourceHome, sourceProfiles, authority, broker) : null;
            _maps = new(_journeys, profiles, authority, broker, Permissions, source);
            _shelf = new(_library, profiles, authority, broker, Permissions, source);
            await ownership.BindNewEmptyAsync(maps ? "maps" : "shelf", (await Settings.GetStoreIdentityAsync(default)).StoreId.ToString("D"));
        }
        public async Task<object> ReviewAsync()
        {
            if (maps)
            {
                var display = await _maps!.LoadForDisplayAsync(Actor);
                return await _maps.ReviewAsync(display.Selection, new(Guid.NewGuid(), "Original journey",
                    [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Original required instruction")],
                    MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0));
            }
            var shelf = await _shelf!.LoadForDisplayAsync(Actor);
            return await _shelf.ReviewAsync(shelf.Selection, new(Guid.NewGuid(), "Original app", new(ShelfTargetKind.InstalledApplication, "original-app")));
        }
        public string RequestID(object review) => maps ? ((IMapsLibraryReview)review).RequestID : ((IShelfLibraryReview)review).RequestID;
        public async Task ApproveAsync(object review) => Assert.True((await Permissions.DecideAsync(RequestID(review), HomeApprovalChoice.Accept)).Succeeded);
        public async Task<(bool Committed, bool AuditRecorded, bool CompletionUnknown)> CommitAsync(object review)
        {
            if (maps) { var r = await _maps!.CommitAsync((IMapsLibraryReview)review); return (r.Committed, r.AuditRecorded, r.CompletionUnknown); }
            var s = await _shelf!.CommitAsync((IShelfLibraryReview)review); return (s.Committed, s.AuditRecorded, s.CompletionUnknown);
        }
        public async Task<(bool Committed, bool AuditRecorded, bool CompletionUnknown)> FinishAsync(object review)
        {
            if (maps) { var r = await _maps!.FinishAsync((IMapsLibraryReview)review); return (r.Committed, r.AuditRecorded, r.CompletionUnknown); }
            var s = await _shelf!.FinishAsync((IShelfLibraryReview)review); return (s.Committed, s.AuditRecorded, s.CompletionUnknown);
        }
        public async Task<object> ReviewSmartCollectionAsync(IReadOnlyList<string> tags,
            IReadOnlyList<ShelfTargetKind> kinds, bool holdIdentity = false)
        {
            var edit = await ReviewItemEditAsync(["work", "reference", "offline"]);
            await ApproveAsync(edit); Assert.True((await CommitAsync(edit)).Committed);
            var next = await _shelf!.LoadForDisplayAsync(Actor);
            var other = await _shelf.ReviewAsync(next.Selection,
                new(Guid.NewGuid(), "Other app", new(ShelfTargetKind.InstalledApplication, "other-app")));
            await ApproveAsync(other); Assert.True((await CommitAsync(other)).Committed);
            var original = await _shelf.LoadForDisplayAsync(Actor); Settings.HoldIdentity = holdIdentity;
            return await _shelf.ReviewCreateSmartCollectionAsync(original.Selection,
                new(Guid.NewGuid(), "Original smart collection", ShelfCollectionKind.Smart,
                    ShelfPresentation.List, new(kinds, tags, true, true)));
        }
        public async Task AssertSmartCollectionCommittedAsync()
        {
            var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(Paths)).ReadAsync();
            var collection = Assert.Single(reopened.Library.Collections);
            Assert.Equal("Original smart collection", collection.Name); Assert.Equal(ShelfCollectionKind.Smart, collection.Kind);
            Assert.Equal(new[] { "work" }, collection.Criteria!.RequiredTags);
            Assert.Equal(new[] { ShelfTargetKind.InstalledApplication }, collection.Criteria.TargetKinds);
            Assert.True(collection.Criteria.FavouritesOnly); Assert.True(collection.Criteria.OfflineCapableOnly);
            Assert.Equal(_originalEditedItem!.Id, Assert.Single(ShelfLibraryPolicy.ResolveCollection(reopened.Library, collection.Id)).Id);
            Assert.Empty(reopened.Library.Memberships); Assert.NotNull(reopened.LastOwnedMutation);
        }
        private ShelfLaunchItem? _originalEditedItem;
        public async Task<object> ReviewItemEditAsync(IReadOnlyList<string> tags, bool holdIdentity = false)
        {
            var seed = await ReviewAsync(); await ApproveAsync(seed); Assert.True((await CommitAsync(seed)).Committed);
            _originalEditedItem = Assert.Single((await _library.ReadAsync()).Library.Items);
            var original = await _shelf!.LoadForDisplayAsync(Actor);
            Settings.HoldIdentity = holdIdentity;
            return await _shelf.ReviewEditItemAsync(original.Selection,
                new(_originalEditedItem.Id, "Pinned original", tags, true, 7, _originalEditedItem.Behaviour));
        }
        public async Task AssertItemEditCommittedAsync()
        {
            var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(Paths)).ReadAsync();
            var actual = Assert.Single(reopened.Library.Items);
            Assert.Equal(_originalEditedItem!.Id, actual.Id); Assert.Equal(_originalEditedItem.Target, actual.Target);
            Assert.Equal("Pinned original", actual.Name); Assert.True(actual.IsFavourite); Assert.Equal(7, actual.Order);
            Assert.Equal(new[] { "work", "reference" }, actual.Tags); Assert.NotNull(reopened.LastOwnedMutation);
        }
        public async Task<object> ReviewCollectionAsync(bool membership)
        {
            var collection = new ShelfCollection(Guid.NewGuid(), "Original manual collection", ShelfCollectionKind.Manual);
            if (membership)
            {
                var itemReview = await ReviewAsync(); await ApproveAsync(itemReview); Assert.True((await CommitAsync(itemReview)).Committed);
                var initialDisplay = await _shelf!.LoadForDisplayAsync(Actor);
                var initialCollection = await _shelf.ReviewCreateCollectionAsync(initialDisplay.Selection, collection);
                await ApproveAsync(initialCollection); Assert.True((await CommitAsync(initialCollection)).Committed);
                var current = await _library.ReadAsync();
                var display = await _shelf.LoadForDisplayAsync(Actor);
                return await _shelf.ReviewAddMembershipAsync(display.Selection, collection.Id, Assert.Single(current.Library.Items).Id);
            }
            var original = await _shelf!.LoadForDisplayAsync(Actor);
            return await _shelf.ReviewCreateCollectionAsync(original.Selection, collection);
        }
        public async Task AssertCollectionCommittedAsync(bool membership)
        {
            var actual = await new ShelfLibraryService(new VersionedAtomicSettingsStore(Paths)).ReadAsync();
            var collection = Assert.Single(actual.Library.Collections);
            Assert.Equal("Original manual collection", collection.Name);
            Assert.NotNull(actual.LastOwnedMutation);
            if (membership)
            {
                var member = Assert.Single(actual.Library.Memberships);
                Assert.Equal(collection.Id, member.CollectionId); Assert.Equal(Assert.Single(actual.Library.Items).Id, member.LaunchItemId);
            }
            else Assert.Empty(actual.Library.Memberships);
        }
        public async Task CompeteCollectionAsync()
        {
            // A controlled independent canonical writer changes the real root revision; this is not a Home grant.
            var independent = new ShelfLibraryService(Settings); var current = await independent.ReadAsync();
            Assert.True((await independent.CreateCollectionAsync(current.Library.Revision,
                new(Guid.NewGuid(), "Controlled competing collection", ShelfCollectionKind.Manual))).Success);
        }
        public async Task CompeteAsync()
        {
            if (maps)
            {
                var other = new MapSavedJourney(Guid.NewGuid(), "Competing journey", [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, "Other")],
                    MapObjectVisibility.Private, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);
                Assert.True((await new MapsJourneyService(Settings).SaveJourneyAsync(0, other)).Success);
            }
            else Assert.True((await new ShelfLibraryService(Settings).AddItemAsync(0, new(Guid.NewGuid(), "Competing app", new(ShelfTargetKind.InstalledApplication, "other-app")))).Success);
        }
        public async Task AssertOneCommittedAsync()
        {
            if (maps) Assert.NotNull(Assert.Single((await _journeys.ReadAsync()).Journeys));
            else Assert.NotNull(Assert.Single((await _library.ReadAsync()).Library.Items));
        }
        public void Dispose() { try { Directory.Delete(Paths.DataDirectory, true); } catch (IOException) { } }
    }
    private sealed class HeldSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore,
        IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
    {
        public bool LoseWriteReturnOnce { get; set; }
        public bool HideAfterLostReturn { get; set; }
        public bool HideLibraryReads { get; set; }
        public int GuardedWriteCalls { get; private set; }
        public bool HoldIdentity { get; set; }
        public bool HoldPublication { get; set; }
        public bool HoldAdmission { get; set; }
        public bool HoldBeforeWrite { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token)
        {
            var identity = await actual.GetStoreIdentityAsync(token);
            if (HoldIdentity) { HoldIdentity = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return identity;
        }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            HideLibraryReads && (key == "shelf.library.v1" || key == "maps.journeys.v1")
                ? Task.FromException<T?>(new IOException("Actual library observation temporarily unavailable after lost return."))
                : actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => actual.ImportAsync(value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token)
            => actual.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken token)
            => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            GuardedWriteCalls++;
            if (HoldBeforeWrite) { HoldBeforeWrite = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            var result = await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, new HeldAdmission(this, admission), token);
            if (result.Exchanged && LoseWriteReturnOnce)
            {
                LoseWriteReturnOnce = false; HideLibraryReads = HideAfterLostReturn;
                throw new IOException("Decorator loses return AFTER actual inner atomic physical publication.");
            }
            return result;
        }
        private sealed class HeldAdmission(HeldSettings owner, ISettingsCommitAdmission actual) : ISettingsCommitAdmission
        {
            public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
            {
                if (owner.HoldAdmission && context.Phase == SettingsCommitPhase.Admission)
                { owner.HoldAdmission = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                if (owner.HoldPublication && context.Phase == SettingsCommitPhase.Publication)
                { owner.HoldPublication = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                return await actual.CheckAsync(context, token);
            }
        }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("astra-owned-shelf-maps-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
