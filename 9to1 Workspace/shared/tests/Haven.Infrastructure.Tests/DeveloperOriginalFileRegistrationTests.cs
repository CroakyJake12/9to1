using Haven.Application;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Real Linux file handles/hash reads and original-task custody. The metadata
/// callbacks and permission issuer are synthetic; no Files write or Home approval is inferred.</summary>
public sealed partial class DeveloperOriginalDirectoryObservationTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task File_metadata_close_joins_same_held_raw_task_before_native_stream_retirement_and_requires_exact_result_pair()
    {
        await using var rig = new Rig(); var token = CancellationToken.None;
        var original = await OriginalFileRegistration(rig, token);
        var handle = OriginalFileRegistrationHandle(original.Preparation);
        var held = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new object(); Entry? entry = null; Task<object>? driver = null; var errors = new List<Exception>();
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalFileRegistrationAsync(actualEntry,
                stream => { Assert.Same(handle, stream.SafeFileHandle); entered.TrySetResult(); return held.Task; }, token), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            var close = original.Preparation.CloseAndDrainAsync(); Assert.Same(close, original.Preparation.CloseAndDrainAsync());
            Assert.False(close.IsCompleted); Assert.False(handle.IsClosed);
            held.TrySetResult(result); Assert.Same(result, await driver); await close; Assert.True(handle.IsClosed);
            Assert.Contains(held.Task, FileRegistrationNestedTasks(original.Preparation));
            Assert.True(original.Source.IsIssuedOriginalFileRegistrationOutcome(original.Preparation, driver, held.Task, result));
            Assert.False(original.Source.IsIssuedOriginalFileRegistrationOutcome(original.Preparation, driver, Task.CompletedTask, result));
            Assert.False(original.Source.IsIssuedOriginalFileRegistrationOutcome(original.Preparation, driver, held.Task, new object()));
            await entry.DisposeAsync(); entry = null;
            await original.Source.ValidateOriginalFileRegistrationOutcomeAsync(original.Preparation, driver, held.Task, result, token);
            Assert.Equal("original source stays in place", File.ReadAllText(rig.File));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            held.TrySetResult(result);
            if (driver is not null) try { await driver; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual held file metadata/native cleanup control failed.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task File_post_await_restored_callback_cannot_join_its_kernel_owner_and_retains_same_nested_raw_task()
    {
        await using var rig = new Rig(); var token = CancellationToken.None;
        var original = await OriginalFileRegistration(rig, token); var context = ExecutionContext.Capture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nested = Task.FromResult(new object()); var result = new object(); Exception? refusal = null;
        Entry? entry = null; Task<object>? driver = null; var errors = new List<Exception>();
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalFileRegistrationAsync(actualEntry,
                async borrowedStream =>
                {
                    entered.TrySetResult(); await release.Task;
                    original.Preparation.RunOriginalFileSourceScope(() => ExecutionContext.Run(context!, _ =>
                    {
                        original.Preparation.RetainOriginalFileTask(nested);
                        try { rig.Source.CloseAndDrainOriginalCapturesAsync().GetAwaiter().GetResult(); }
                        catch (Exception error) { refusal = error; }
                    }, null));
                    await nested; return result;
                }, token), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token); Assert.False(driver.IsCompleted);
            release.TrySetResult(); Assert.Same(result, await driver); Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains(nested, FileRegistrationNestedTasks(original.Preparation));
            await original.Preparation.CloseAndDrainAsync(); Assert.True(OriginalFileRegistrationHandle(original.Preparation).IsClosed);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (driver is not null) try { await driver; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual post-await file callback/native cleanup control failed.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Faulted_file_metadata_oce_and_sibling_preserve_exact_faults_without_receipt_or_replay()
    {
        await using var rig = new Rig(); var token = CancellationToken.None;
        var original = await OriginalFileRegistration(rig, token); var handle = OriginalFileRegistrationHandle(original.Preparation);
        var first = new OperationCanceledException("Actual faulted file metadata, not a canceled task.");
        var second = new IOException("Actual file metadata sibling.");
        var raw = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously); raw.SetException([first, second]);
        Entry? entry = null; Task<object>? driver = null; var errors = new List<Exception>();
        bool Expected(Exception error) => Causes(error).All(value => ReferenceEquals(value, first) || ReferenceEquals(value, second));
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalFileRegistrationAsync(actualEntry, _ => raw.Task, token), token);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => driver); Assert.True(driver.IsFaulted);
            Assert.Contains(Causes(failure), value => ReferenceEquals(value, first)); Assert.Contains(Causes(failure), value => ReferenceEquals(value, second));
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync());
            Assert.Contains(Causes(closeFailure), value => ReferenceEquals(value, first)); Assert.Contains(Causes(closeFailure), value => ReferenceEquals(value, second));
            Assert.True(handle.IsClosed); Assert.False(original.Source.IsIssuedOriginalFileRegistrationOutcome(original.Preparation, driver, raw.Task, null));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = original.Preparation.RunOriginalFileRegistrationAsync(actualEntry, _ => raw.Task, token); });
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (driver is not null) try { await driver; } catch (Exception error) { if (!Expected(error)) errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { if (!Expected(error)) errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual file metadata fault/cause/cleanup control failed.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Genuine_canceled_file_metadata_remains_canceled_and_native_cleanup_cannot_issue_an_outcome()
    {
        await using var rig = new Rig(); var token = CancellationToken.None;
        var original = await OriginalFileRegistration(rig, token); var handle = OriginalFileRegistrationHandle(original.Preparation);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var raw = Task.FromCanceled<object>(cancellation.Token);
        Entry? entry = null; Task<object>? driver = null; var errors = new List<Exception>();
        bool Expected(Exception error) => Causes(error).All(value => value is OperationCanceledException);
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalFileRegistrationAsync(actualEntry, _ => raw, token), token);
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver);
            Assert.True(driver.IsCanceled); Assert.Equal(cancellation.Token, canceled.CancellationToken);
            await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync());
            Assert.True(handle.IsClosed); Assert.False(original.Source.IsIssuedOriginalFileRegistrationOutcome(original.Preparation, driver, raw, null));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (driver is not null) try { await driver; } catch (Exception error) { if (!Expected(error)) errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { if (!Expected(error)) errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual canceled file metadata/native cleanup control failed.", errors);
    }

    private static async Task<(IDeveloperProjectOriginalFileRegistrationSource Source,
        IDeveloperProjectOriginalFileRegistrationPreparation Preparation, Permission Permission, DeveloperProjectSetupStep Step)>
        OriginalFileRegistration(Rig rig, CancellationToken token)
    {
        var capture = await rig.Capture(token); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var source = (IDeveloperProjectOriginalFileRegistrationSource)rig.Source;
        var step = intent.Steps.Single(value => value.Kind == DeveloperProjectSetupStepKind.RegisterExistingFileMetadata);
        return (source, await source.PrepareOriginalFileRegistrationAsync(intent, capture, permission, step, token), permission, step);
    }
    private static SafeFileHandle OriginalFileRegistrationHandle(IDeveloperProjectOriginalFileRegistrationPreparation preparation)
        => (SafeFileHandle)preparation.GetType().GetField("Handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(preparation)!;
    private static IEnumerable<Task> FileRegistrationNestedTasks(IDeveloperProjectOriginalFileRegistrationPreparation preparation)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var operation = preparation.GetType().GetField("Operation", flags)!.GetValue(preparation)!;
        var original = operation.GetType().GetField("Original", flags)!.GetValue(operation)!;
        return ((IEnumerable<Task>)original.GetType().GetField("Sources", flags)!.GetValue(original)!).ToArray();
    }
}
