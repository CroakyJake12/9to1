using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Late_original_attachment_callback_survives_successful_command_pruning_and_blocks_new_admission() =>
        RunAttachmentBoundaryControl(async control =>
        {
            var neutral = ExecutionContext.Capture()!;
            var current = await control.Rig.Bridge.ReadConversationAsync(control.Binding, Token);
            var draft = Assert.IsType<ConversationDraft>(current.Draft);
            var ids = JsonSerializer.Deserialize<Guid[]>(draft.AttachmentIdsJson)!;
            Assert.Single(ids);
            Action? escaped = null;
            var first = control.Graph.Source.PrepareOriginalAttachmentInputWithinSourceAsync(control.Binding,
                draft.Content, ids, body => { escaped ??= body; body(); }, control.Retain, Token);
            control.Retain(first); var input = await first;
            Assert.True(control.Graph.Source.IsIssuedOriginalAttachmentInput(input)); Assert.NotNull(escaped);
            // A second real successful admission prunes the healthy first driver.
            // Neither success authorizes invoking its expired finite callback.
            var next = control.Graph.Source.PrepareOriginalAttachmentInputWithinSourceAsync(control.Binding,
                draft.Content, ids, Scope, control.Retain, Token);
            control.Retain(next); Assert.NotSame(input, await next);
            Exception? actual = null;
            ExecutionContext.Run(neutral, _ => actual = Record.Exception(() => escaped!()), null);
            control.ExpectOnly(actual, typeof(InvalidOperationException),
                "The original memory callback expired, repeated or moved threads.");
            var calls = 0;
            var refused = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = control.Graph.Source.PrepareOriginalAttachmentInputWithinSourceAsync(control.Binding,
                    draft.Content, ids, body => { calls++; body(); }, control.Retain, Token);
            });
            Assert.Same(actual, refused); Assert.Equal(0, calls);
            var unchangedBytes = await File.ReadAllBytesAsync(control.Path, Token);
            Assert.True(control.Bytes.SequenceEqual(unchangedBytes));
            // The shared genuine fixture independently acquires and joins SAME
            // cached failed Source/graph/Rig closes, requiring this exact cause.
        });
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Already_admitted_attachment_read_refuses_after_old_callback_fault_and_closes_actual_lease() =>
        RunAttachmentBoundaryControl(async control =>
        {
            var neutral = ExecutionContext.Capture()!;
            var current = await control.Rig.Bridge.ReadConversationAsync(control.Binding, Token);
            var draft = Assert.IsType<ConversationDraft>(current.Draft);
            var ids = JsonSerializer.Deserialize<Guid[]>(draft.AttachmentIdsJson)!;
            Action? escaped = null;
            var first = control.Graph.Source.PrepareOriginalAttachmentInputWithinSourceAsync(control.Binding,
                draft.Content, ids, body => { escaped ??= body; body(); }, control.Retain, Token);
            control.Retain(first); await first; Assert.NotNull(escaped);
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(false);
            var rawGate = new object(); var actuals = new List<Task>(); var held = 0; var productiveAfterFault = 0;
            Haven.Infrastructure.CanonicalSqliteOriginalStoreLease? heldLease = null;
            Exception? originalFailure = null;
            void Retain(Task raw)
            {
                lock (rawGate) { actuals.Add(raw); control.Retain(raw); }
            }
            void ActualScope(Action body)
            {
                bool snapshotReady;
                lock (rawGate)
                {
                    snapshotReady = actuals.OfType<Task<Haven.Infrastructure.ConversationProductionRepository.OriginalAttachmentInputSnapshot>>()
                        .Any(raw => raw.IsCompletedSuccessfully);
                    if (snapshotReady)
                        heldLease = actuals.OfType<Task<Haven.Infrastructure.CanonicalSqliteOriginalStoreLease>>()
                            .Last(raw => raw.IsCompletedSuccessfully).GetAwaiter().GetResult();
                }
                if (snapshotReady && Interlocked.CompareExchange(ref held, 1, 0) == 0)
                {
                    reached.SetResult(); release.Wait(Token);
                    // This is the SAME still-active source callback. Its body must
                    // check owner health before the next productive getter/factory.
                    body(); Interlocked.Increment(ref productiveAfterFault); return;
                }
                body();
            }
            var second = Task.Run(async () =>
            {
                var raw = control.Graph.Source.PrepareOriginalAttachmentInputWithinSourceAsync(control.Binding,
                    draft.Content, ids, ActualScope, Retain, Token);
                Retain(raw); return await raw;
            }, Token);
            Retain(second);
            static bool OnlySameOccurrence(Exception? actual, Exception expected) =>
                ReferenceEquals(actual, expected) || actual is AggregateException { InnerExceptions.Count: > 0 } group &&
                    group.InnerExceptions.All(cause => OnlySameOccurrence(cause, expected));
            var failures = new List<Exception>(); var expectedRegistered = false;
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                ExecutionContext.Run(neutral, _ => originalFailure = Record.Exception(() => escaped!()), null);
                control.ExpectOnly(originalFailure, typeof(InvalidOperationException),
                    "The original memory callback expired, repeated or moved threads.");
                expectedRegistered = true;
            }
            catch (Exception cause) { failures.Add(cause); }
            finally
            {
                release.Set();
                // Even an earlier assertion/timeout must independently join the
                // actual command and acquire/join the SAME captured lease close.
                Exception? refused = null;
                try { _ = await second; }
                catch (Exception cause) { refused = second.Exception ?? cause; }
                try
                {
                    if (expectedRegistered)
                    {
                        Assert.NotNull(originalFailure);
                        Assert.True(OnlySameOccurrence(refused, originalFailure));
                        control.ExpectOnly(refused, typeof(InvalidOperationException),
                            "The original memory callback expired, repeated or moved threads.");
                    }
                    else if (refused is not null) failures.Add(refused);
                    Assert.Equal(0, productiveAfterFault);
                }
                catch (Exception cause) { failures.Add(cause); }
                Task? closed = null;
                try
                {
                    Haven.Infrastructure.CanonicalSqliteOriginalStoreLease? actualLease;
                    lock (rawGate) actualLease = heldLease;
                    var lease = Assert.IsType<Haven.Infrastructure.CanonicalSqliteOriginalStoreLease>(actualLease);
                    closed = lease.CloseAndDrainAsync(); Retain(closed);
                    await closed; Assert.True(closed.IsCompletedSuccessfully);
                    var same = lease.CloseAndDrainAsync(); Retain(same);
                    Assert.Same(closed, same); await same;
                }
                catch (Exception cause) { failures.Add(closed?.Exception ?? cause); }
            }
            try
            {
                var unchangedBytes = await File.ReadAllBytesAsync(control.Path, Token);
                Assert.True(control.Bytes.SequenceEqual(unchangedBytes));
            }
            catch (Exception cause) { failures.Add(cause); }
            if (failures.Count != 0)
                throw new AggregateException("The held attachment control or its independent original joins failed.", failures);
        });

}
