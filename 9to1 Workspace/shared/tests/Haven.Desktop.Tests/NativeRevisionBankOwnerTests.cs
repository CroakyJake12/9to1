using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

/// <summary>Shown headless controls over an actual guarded temporary settings owner.
/// These authored cases are not installed Windows, Home receipt or source-open evidence.</summary>
public sealed class NativeRevisionBankOwnerTests
{
    [AvaloniaFact]
    public Task Native_membership_and_categories_keep_the_canonical_source() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var page = profile.CreatePage();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            Invoke(Find(page, "Add Bank source " + profile.Reference.ContextId.ToString("D")));
            var admitted = Assert.IsAssignableFrom<Task>(page.LastOriginalTask);
            await admitted;
            Assert.Same(admitted, page.LastOriginalTask);
            var fresh = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, TestContext.Current.CancellationToken);
            Assert.Equal(profile.Reference.ContextId, Assert.Single(fresh!.Data.Members).ResourceId);
            Assert.Equal(profile.Reference, Assert.Single((await profile.Reopen().ReadExistingAsync(profile.Space.Id,
                TestContext.Current.CancellationToken))!.ContextReferences!));

            var name = Assert.Single(page.Scene.Root!.DescendantsAndSelf().OfType<Input>(),
                input => input.Name == "RevisionBankCategoryName");
            name.Text = "Mechanics";
            Invoke(Find(page, "Create Revision Bank category"));
            await Assert.IsAssignableFrom<Task>(page.LastOriginalTask);
            fresh = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, TestContext.Current.CancellationToken);
            var category = Assert.Single(fresh!.Data.Categories, item => item.Name == "Mechanics");
            Assert.False(category.IsBuiltIn);
            Invoke(Find(page, "Classify Bank source " + profile.Reference.ContextId.ToString("D") + " as Mechanics"));
            await Assert.IsAssignableFrom<Task>(page.LastOriginalTask);
            fresh = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, TestContext.Current.CancellationToken);
            Assert.Equal(category.CategoryId, Assert.Single(Assert.Single(fresh!.Data.Members).CategoryIds));
            Invoke(Find(page, "Remove Bank source " + profile.Reference.ContextId.ToString("D")));
            await Assert.IsAssignableFrom<Task>(page.LastOriginalTask);
            fresh = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, TestContext.Current.CancellationToken);
            Assert.Empty(fresh!.Data.Members);
            Assert.Equal(profile.Reference, Assert.Single((await profile.Reopen().ReadExistingAsync(profile.Space.Id,
                TestContext.Current.CancellationToken))!.ContextReferences!));
            Assert.Equal("source sentinel", await File.ReadAllTextAsync(profile.SourceSentinel, TestContext.Current.CancellationToken));
        });

    [AvaloniaFact]
    public Task Delayed_physical_acknowledgment_is_retained_before_the_same_close_finishes() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var page = profile.CreatePage();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var hold = profile.Store.HoldNextOriginalReturn();
            Invoke(Find(page, "Add Bank source " + profile.Reference.ContextId.ToString("D")));
            var outer = Assert.IsAssignableFrom<Task>(page.LastOriginalTask);
            var physical = await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.True(physical.Exchanged);
            Assert.Same(physical, hold.OriginalAcknowledgedResult);
            Assert.True(Assert.IsAssignableFrom<Task>(hold.OriginalOwnerTask).IsCompletedSuccessfully);
            var inner = Assert.IsAssignableFrom<Task<RevisionBankMutationResult>>(page.LastOriginalMutationTask);
            Assert.False(inner.IsCompleted);
            Assert.False(outer.IsCompleted);
            Assert.Null(page.LastAcknowledgedOriginalMutation);
            var fresh = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, TestContext.Current.CancellationToken);
            Assert.Equal(profile.Space.Revision + 1, fresh!.SpaceRevision);
            Assert.Equal(profile.Reference.ContextId, Assert.Single(fresh.Data.Members).ResourceId);

            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.True(page.IsRetiring);
            hold.Release.TrySetResult();
            await outer;
            var actualAcknowledgment = await inner;
            await close;
            Assert.Same(actualAcknowledgment, page.LastAcknowledgedOriginalMutation);
            Assert.Equal(fresh.SpaceRevision, actualAcknowledgment.SpaceRevision);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.Null(page.Content);
        });

    [AvaloniaFact]
    public Task Stale_expected_revision_retains_its_original_conflict_and_does_not_write() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var page = profile.CreatePage();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var foreignOwner = new SpaceRegistry(new VersionedAtomicSettingsStore(profile), profile.Authority.CaptureWriteAdmissionAsync);
            var foreign = await foreignOwner.MutateRevisionBankAsync(new(profile.Space.Id, profile.Space.Revision,
                Guid.NewGuid(), RevisionBankMutationKind.CreateCategory, CategoryId: Guid.NewGuid(), CategoryName: "Foreign"),
                TestContext.Current.CancellationToken);
            var before = await profile.ReadBytesAsync();
            var original = page.MutateOriginalAsync(new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
                RevisionBankMutationKind.Add, profile.Reference.ContextId), TestContext.Current.CancellationToken);
            var failure = Assert.IsType<SpaceRevisionConflictException>(await Failure(original));
            Assert.Equal(profile.Space.Id, failure.SpaceId);
            Assert.Equal(profile.Space.Revision, failure.ExpectedRevision);
            Assert.Equal(foreign.SpaceRevision, failure.ActualRevision);
            Assert.Equal(before, await profile.ReadBytesAsync());
            Assert.Null(page.LastAcknowledgedOriginalMutation);
            Assert.Contains(original, page.OriginalTasks);
            var close = page.CloseAndDrainAsync();
            var closeFailure = await Failure(close);
            Assert.Same(failure, closeFailure);
            profile.ObserveExactExpected(failure);
            profile.ObserveExactExpected(closeFailure);
            Assert.Equal(before, await profile.ReadBytesAsync());
        });

    [AvaloniaFact]
    public Task Faulted_original_tasks_use_the_finite_capacity_without_losing_source_bytes() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var page = profile.CreatePage(capacity: 2);
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var before = await profile.ReadBytesAsync();
            var first = page.MutateOriginalAsync(new(Guid.NewGuid(), profile.Space.Revision, Guid.NewGuid(),
                RevisionBankMutationKind.Add, profile.Reference.ContextId), TestContext.Current.CancellationToken);
            var firstFailure = Assert.IsType<UnauthorizedAccessException>(await Failure(first));
            var second = page.MutateOriginalAsync(new(Guid.NewGuid(), profile.Space.Revision, Guid.NewGuid(),
                RevisionBankMutationKind.Add, profile.Reference.ContextId), TestContext.Current.CancellationToken);
            var secondFailure = Assert.IsType<UnauthorizedAccessException>(await Failure(second));
            Assert.NotSame(firstFailure, secondFailure);
            Assert.Equal(new[] { first, second }, page.OriginalTasks);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = page.RefreshOriginalAsync(TestContext.Current.CancellationToken);
            });
            Assert.Equal(before, await profile.ReadBytesAsync());
            var close = page.CloseAndDrainAsync();
            var closeFailure = Assert.IsType<AggregateException>(await Failure(close));
            Assert.Equal(new Exception[] { firstFailure, secondFailure }, closeFailure.InnerExceptions);
            Assert.Equal(new[] { first, second }, page.OriginalTasks);
            Assert.Same(close, page.CloseAndDrainAsync());
            profile.ObserveExactExpected(closeFailure);
            Assert.Equal(before, await profile.ReadBytesAsync());
        });

    [AvaloniaFact]
    public Task Native_delete_uses_the_original_owner_once_and_retains_its_pending_return() =>
        RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            Task? actualOwner = null;
            async Task DeleteOnce(Guid id)
            {
                calls++;
                Assert.Equal(profile.Space.Id, id);
                actualOwner = profile.Owner.Registry.DeleteAsync(id, TestContext.Current.CancellationToken);
                await actualOwner;
                entered.TrySetResult();
                await release.Task;
            }
            NativeSpacesPage? native = null;
            Window? window = null;
            Task? original = null;
            var failures = new List<Exception>();
            try
            {
                native = new NativeSpacesPage(profile.Owner.Registry, null, null, deleteSpace: DeleteOnce);
                window = new Window();
                window.Width = 960; window.Height = 720; window.Content = native;
                window.Show();
                await native.ActivateAsync(TestContext.Current.CancellationToken);
                Invoke(Assert.Single(native.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>(),
                    button => button.Accessibility.AccessibleName == "Open Space " + profile.Space.Name));
                Invoke(Assert.Single(native.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>(), button => button.Name == "Delete"));
                Invoke(Assert.Single(native.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>(), button => button.Content == "Delete permanently"));
                original = Assert.IsAssignableFrom<Task>(native.LastOriginalBankOrDeleteTask);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                Assert.Equal(1, calls);
                Assert.True(Assert.IsAssignableFrom<Task>(actualOwner).IsCompletedSuccessfully);
                Assert.False(original.IsCompleted);
                Assert.Null(await profile.Reopen().ReadExistingAsync(profile.Space.Id, TestContext.Current.CancellationToken));
                var close = native.CloseOriginalBankAndDeleteActionsAsync();
                Assert.Same(close, native.CloseOriginalBankAndDeleteActionsAsync());
                Assert.False(close.IsCompleted);
                release.TrySetResult();
                await original;
                await close;
                Assert.Equal(1, calls);
                Assert.True(close.IsCompletedSuccessfully);
            }
            catch (Exception primary) { Add(primary); }
            finally
            {
                release.TrySetResult();
                if (original is not null) { try { await original; } catch (Exception error) { Add(error); } }
                if (native is not null)
                {
                    try { await native.CloseOriginalBankAndDeleteActionsAsync(); } catch (Exception error) { Add(error); }
                    try { native.Dispose(); } catch (Exception error) { Add(error); }
                }
                if (window is not null) { try { window.Close(); } catch (Exception error) { Add(error); } }
            }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException(failures);
            void Add(Exception error) { if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error); }
        });

    [AvaloniaFact]
    public async Task Real_native_notification_reentry_withdraws_remaining_writes_and_rejects_replaced_owner()
    {
        await RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var page = profile.CreatePage();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var content = page.OriginalScene.OriginalContent;
            var count = content.Children.Count;
            var before = await profile.ReadBytesAsync();
            var notifications = 0;
            Task? originalClose = null;
            content.Invalidated += (_, _) =>
            {
                if (++notifications != 1) return;
                page.RequestRetirement();
                originalClose = page.CloseAndDrainAsync();
            };
            var original = page.RefreshOriginalAsync(TestContext.Current.CancellationToken);
            await original;
            await Assert.IsAssignableFrom<Task>(originalClose);
            Assert.Equal(1, notifications);
            Assert.Equal(count - 1, content.Children.Count);
            Assert.Equal(before, await profile.ReadBytesAsync());
        });

        await RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var page = profile.CreatePage();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var status = page.OriginalScene.OriginalStatus;
            Assert.Equal(HavenVisibility.Collapsed, status.GetValue(HavenProperties.Visibility));
            var notifications = 0;
            Task? originalClose = null;
            status.Invalidated += (_, _) =>
            {
                if (++notifications != 1) return;
                Assert.NotNull(page.LastAcknowledgedOriginalMutation);
                page.RequestRetirement();
                originalClose = page.CloseAndDrainAsync();
            };
            Invoke(Find(page, "Add Bank source " + profile.Reference.ContextId.ToString("D")));
            var original = Assert.IsAssignableFrom<Task>(page.LastOriginalTask);
            await original;
            await Assert.IsAssignableFrom<Task>(originalClose);
            Assert.Equal(1, notifications);
            Assert.Equal(HavenVisibility.Collapsed, status.GetValue(HavenProperties.Visibility));
            var acknowledged = await Assert.IsAssignableFrom<Task<RevisionBankMutationResult>>(page.LastOriginalMutationTask);
            Assert.Same(acknowledged, page.LastAcknowledgedOriginalMutation);
            Assert.Equal(acknowledged.SpaceRevision,
                (await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, TestContext.Current.CancellationToken))!.SpaceRevision);
        });

        await RevisionBankOriginalFixture.RunAsync(async profile =>
        {
            var page = profile.CreatePage();
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var originalOwner = profile.Owner;
            var content = page.OriginalScene.OriginalContent;
            var count = content.Children.Count;
            var before = await profile.ReadBytesAsync();
            var notifications = 0;
            content.Invalidated += (_, _) =>
            {
                if (++notifications == 1) profile.ReplaceWithSameGuardedOwnerSession();
            };
            var original = page.RefreshOriginalAsync(TestContext.Current.CancellationToken);
            var failure = Assert.IsType<AggregateException>(await Failure(original));
            Assert.Equal(2, failure.InnerExceptions.Count);
            Assert.All(failure.InnerExceptions, cause => Assert.IsType<UnauthorizedAccessException>(cause));
            Assert.NotSame(failure.InnerExceptions[0], failure.InnerExceptions[1]);
            Assert.NotSame(originalOwner, profile.Owner);
            Assert.Same(originalOwner.Settings, profile.Owner.Settings);
            Assert.Equal(1, notifications);
            Assert.Equal(count - 1, content.Children.Count);
            var closeFailure = await Failure(page.CloseAndDrainAsync());
            Assert.Same(failure, closeFailure);
            Assert.Contains(original, page.OriginalTasks);
            Assert.Equal(before, await profile.ReadBytesAsync());
            profile.ObserveExactExpected(failure);
            profile.ObserveExactExpected(closeFailure);
        });
    }

    private static HavenButton Find(RevisionBankPage page, string accessibleName) =>
        Assert.Single(page.Scene.Root!.DescendantsAndSelf().OfType<HavenButton>(),
            button => button.Accessibility.AccessibleName == accessibleName);

    private static void Invoke(HavenButton button)
    {
        // Invoke the same maintained native element method; no replacement Page delegate.
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(button, null);
    }

    private static async Task<Exception> Failure(Task original)
    {
        try { await original; }
        catch (Exception error) { return error; }
        throw new Xunit.Sdk.XunitException("The exact original task unexpectedly succeeded.");
    }
}
