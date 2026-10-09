using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalNativeColdProjectCompositionTests
{
    // Reuses the real configured Home/Files/SQLite/native READ fixture. These are
    // Home source controls, not Task dispatch, Chat capture or installer evidence.
    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Prepared_live_input_revalidates_twice_without_another_review_or_input_close()
        => WithClosedInput(async rig =>
        {
            var input = await rig.PrepareAsync();
            var preparation = input.OriginalPreparation; var identity = input.OriginalIdentity;
            for (var check = 0; check < 2; check++)
            {
                var actual = rig.Source.ValidatePreparedOriginalProjectInputWithinSourceAsync(input,
                    input.OriginalConversation, input.OriginalContainer, identity.OriginalProjectContextJson,
                    body => body(), rig.Retain, rig.Token);
                rig.Retain(actual); await actual;
                Assert.True(actual.IsCompletedSuccessfully);
                Assert.True(rig.Source.IsIssuedOriginalProjectInput(input));
                Assert.Same(preparation, input.OriginalPreparation); Assert.Same(identity, input.OriginalIdentity);
                Assert.False(rig.Source.TryObserveClosedOwnedOriginalProjectInput(input, out _));
            }
            Assert.Single(rig.ApprovedRequestIds);
            var current = await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token);
            Assert.Empty(current.Grants);
            Assert.DoesNotContain(current.PendingRequests,
                request => request.Scope.ActionName == HomeColdProjectReadReconciliation.ReadAction);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Prepared_input_validation_does_not_adopt_changed_actual_saved_project_bytes()
        => WithClosedInput(async rig =>
        {
            var input = await rig.PrepareAsync(); var identity = input.OriginalIdentity;
            var changed = await rig.Store.SaveAsync(rig.Workspace with
            {
                Projects = [rig.Workspace.Projects[0] with { Name = "changed live prepared project" }]
            }, rig.Workspace.Revision, rig.Token);
            Assert.True(changed.Succeeded);
            var actual = rig.Source.ValidatePreparedOriginalProjectInputWithinSourceAsync(input,
                input.OriginalConversation, input.OriginalContainer, identity.OriginalProjectContextJson,
                body => body(), rig.Retain, rig.Token);
            rig.Retain(actual);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            rig.KeepExpected(actual, failure);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Same(identity, input.OriginalIdentity); Assert.Single(rig.ApprovedRequestIds);
            Assert.False(rig.Source.TryObserveClosedOwnedOriginalProjectInput(input, out _));
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Prepared_validation_retention_callback_keeps_actual_source_and_input_self_join_guards()
        => WithClosedInput(async rig =>
        {
            var input = await rig.PrepareAsync();
            var lease = Assert.IsAssignableFrom<ITaskRunColdProjectRestorationLease>(input);
            var prior = ExecutionContext.Capture()!; var callbacks = 0;
            void Retain(Task raw)
            {
                rig.Retain(raw);
                ExecutionContext.Run(prior.CreateCopy(), _ =>
                {
                    callbacks++;
                    Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainOriginalAsync(); });
                    Assert.Throws<InvalidOperationException>(() => { _ = lease.CloseAndDrainOriginalAsync(); });
                }, null);
            }
            var actual = rig.Source.ValidatePreparedOriginalProjectInputWithinSourceAsync(input,
                input.OriginalConversation, input.OriginalContainer, input.OriginalIdentity.OriginalProjectContextJson,
                body => body(), Retain, rig.Token);
            rig.Retain(actual); await actual;
            Assert.True(callbacks > 0); Assert.True(rig.Source.IsIssuedOriginalProjectInput(input));
            Assert.False(rig.Source.TryObserveClosedOwnedOriginalProjectInput(input, out _));
        });
}
