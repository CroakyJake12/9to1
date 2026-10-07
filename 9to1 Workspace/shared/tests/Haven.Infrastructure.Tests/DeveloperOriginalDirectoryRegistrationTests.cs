using Haven.Application;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual maintained Linux descriptor and whole raw metadata-task custody.
/// Metadata callbacks and configured Home issuers are synthetic; no Files write, consent,
/// complete setup or Windows acceptance is inferred from these controls.</summary>
public sealed partial class DeveloperOriginalDirectoryObservationTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Registration_close_joins_exact_held_raw_metadata_before_descriptor_retirement_and_preserves_private_result_pairing()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token;
        var original = await OriginalRegistration(rig, token);
        var held = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new object(); Entry? entry = null; Task<object>? driver = null; Task? close = null; var errors = new List<Exception>();
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalDirectoryRegistrationAsync(actualEntry,
                () => { entered.TrySetResult(); return held.Task; }, token), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            close = original.Preparation.CloseAndDrainAsync(); Assert.Same(close, original.Preparation.CloseAndDrainAsync());
            Assert.False(close.IsCompleted); Assert.False(OriginalRegistrationHandle(original.Preparation).IsClosed);
            held.TrySetResult(result); Assert.Same(result, await driver); await close; Assert.True(OriginalRegistrationHandle(original.Preparation).IsClosed);
            Assert.True(original.Source.IsIssuedOriginalDirectoryRegistrationOutcome(original.Preparation, driver, held.Task, result));
            Assert.False(original.Source.IsIssuedOriginalDirectoryRegistrationOutcome(original.Preparation, driver, Task.CompletedTask, result));
            Assert.False(original.Source.IsIssuedOriginalDirectoryRegistrationOutcome(original.Preparation, driver, held.Task, new object()));
            await entry.DisposeAsync(); entry = null;
            await original.Source.ValidateOriginalDirectoryRegistrationOutcomeAsync(original.Preparation, driver, held.Task, result, token);
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
        if (errors.Count != 0) throw new AggregateException("Original held metadata/lease control and independent cleanup.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Registration_actual_faulted_oce_and_sibling_remain_original_faults_without_private_result_or_replay()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token; var original = await OriginalRegistration(rig, token);
        var first = new OperationCanceledException("Original raw metadata fault, no canceled Task."); var second = new IOException("Original raw metadata sibling.");
        var raw = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously); raw.SetException([first, second]);
        Entry? entry = null; Task<object>? driver = null; var errors = new List<Exception>();
        bool Expected(Exception error) => Causes(error).All(value => ReferenceEquals(value, first) || ReferenceEquals(value, second));
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalDirectoryRegistrationAsync(actualEntry, () => raw.Task, token), token);
            var fault = await Assert.ThrowsAnyAsync<Exception>(() => driver); Assert.True(driver.IsFaulted);
            Assert.Contains(Causes(fault), value => ReferenceEquals(value, first)); Assert.Contains(Causes(fault), value => ReferenceEquals(value, second));
            var closeFault = await Assert.ThrowsAnyAsync<Exception>(() => original.Preparation.CloseAndDrainAsync());
            Assert.Contains(Causes(closeFault), value => ReferenceEquals(value, first)); Assert.Contains(Causes(closeFault), value => ReferenceEquals(value, second));
            Assert.True(OriginalRegistrationHandle(original.Preparation).IsClosed);
            Assert.False(original.Source.IsIssuedOriginalDirectoryRegistrationOutcome(original.Preparation, driver, raw.Task, null));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = original.Preparation.RunOriginalDirectoryRegistrationAsync(actualEntry, () => raw.Task, token); });
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (driver is not null) try { await driver; } catch (Exception error) { if (!Expected(error)) errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { if (!Expected(error)) errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual faulted metadata original/independent cleanup control failed.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Restored_metadata_factory_cannot_join_same_kernel_registration_original_and_second_body_is_refused()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token; var original = await OriginalRegistration(rig, token);
        var oldContext = ExecutionContext.Capture(); Exception? refusal = null; Entry? entry = null; Task<object>? driver = null;
        var result = new object(); var raw = Task.FromResult(result); var calls = 0; var errors = new List<Exception>();
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalDirectoryRegistrationAsync(actualEntry, () =>
            {
                calls++; ExecutionContext.Run(oldContext!, _ =>
                { try { rig.Source.CloseAndDrainOriginalCapturesAsync().GetAwaiter().GetResult(); } catch (Exception error) { refusal = error; } }, null);
                return raw;
            }, token), token);
            Assert.Same(result, await driver); Assert.IsType<InvalidOperationException>(refusal);
            Assert.Throws<UnauthorizedAccessException>(() => { _ = original.Preparation.RunOriginalDirectoryRegistrationAsync(actualEntry, () => { calls++; return raw; }, token); });
            Assert.Equal(1, calls); await original.Preparation.CloseAndDrainAsync();
            Assert.True(original.Source.IsIssuedOriginalDirectoryRegistrationOutcome(original.Preparation, driver, raw, result));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (driver is not null) try { await driver; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Restored actual metadata callback/lease cleanup control failed.", errors);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Post_await_restored_source_callback_uses_same_kernel_scope_and_retains_exact_nested_task()
    {
        using var directoryLifetime = new CancellationTokenSource();
        await using var rig = new Rig(); var token = directoryLifetime.Token;
        var original = await OriginalRegistration(rig, token); var oldContext = ExecutionContext.Capture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nested = Task.FromResult(new object()); var result = new object(); Exception? refusal = null;
        Entry? entry = null; Task<object>? driver = null; var errors = new List<Exception>();
        try
        {
            var actualEntry = await original.Permission.EnterOriginalStepAsync(original.Step, token); entry = actualEntry;
            driver = actualEntry.RunOriginalStep(original.Step, () => original.Preparation.RunOriginalDirectoryRegistrationAsync(actualEntry,
                async () =>
                {
                    entered.TrySetResult(); await release.Task;
                    original.Preparation.RunOriginalDirectorySourceScope(() => ExecutionContext.Run(oldContext!, _ =>
                    {
                        original.Preparation.RetainOriginalDirectoryTask(nested);
                        try { rig.Source.CloseAndDrainOriginalCapturesAsync().GetAwaiter().GetResult(); }
                        catch (Exception error) { refusal = error; }
                    }, null));
                    await nested; return result;
                }, token), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token); Assert.False(driver.IsCompleted);
            release.TrySetResult(); Assert.Same(result, await driver);
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains(nested, RegistrationNestedTasks(original.Preparation));
            await original.Preparation.CloseAndDrainAsync();
            Assert.True(OriginalRegistrationHandle(original.Preparation).IsClosed);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (driver is not null) try { await driver; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await original.Preparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual post-await source callback/original raw task cleanup failed.", errors);
    }

    private static Task[] RegistrationNestedTasks(IDeveloperProjectOriginalDirectoryRegistrationPreparation original)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var operation = original.GetType().GetField("Operation", flags)!.GetValue(original)!;
        var owner = operation.GetType().GetField("Original", flags)!.GetValue(operation)!;
        return ((List<Task>)owner.GetType().GetField("Sources", flags | System.Reflection.BindingFlags.Public)!.GetValue(owner)!).ToArray();
    }

    private static SafeFileHandle OriginalRegistrationHandle(IDeveloperProjectOriginalDirectoryRegistrationPreparation original)
    {
        var owner = original.GetType().GetProperty("Actual", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(original)!;
        return (SafeFileHandle)owner.GetType().GetField("Handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
    }
    private static async Task<(IDeveloperProjectOriginalDirectoryRegistrationSource Source,
        IDeveloperProjectOriginalDirectoryRegistrationPreparation Preparation, Permission Permission, DeveloperProjectSetupStep Step)>
        OriginalRegistration(Rig rig, CancellationToken token)
    {
        var capture = await rig.Capture(token); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var observedPreparation = await rig.Directories.PrepareOriginalDirectoryAsync(intent, capture, permission, intent.Steps[1], token);
        Entry? entry = null; Task<IDeveloperProjectOriginalDirectoryObservation>? actual = null;
        IDeveloperProjectOriginalDirectoryObservation? observed = null; var errors = new List<Exception>();
        try
        {
            entry = await permission.EnterOriginalStepAsync(intent.Steps[1], token);
            actual = entry.RunOriginalStep(intent.Steps[1], () => observedPreparation.ObserveOriginalDirectoryAsync(entry, token), token);
            observed = await actual;
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (actual is not null) try { await actual; } catch (Exception error) { errors.Add(error); }
            if (entry is not null) try { await entry.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { await observedPreparation.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original predecessor observation/independent cleanup failed.", errors);
        var source = (IDeveloperProjectOriginalDirectoryRegistrationSource)rig.Source;
        var preparation = await source.PrepareOriginalDirectoryRegistrationAsync(intent, capture, permission, intent.Steps[2], observedPreparation, actual!, observed!, token);
        return (source, preparation, permission, intent.Steps[2]);
    }
}
