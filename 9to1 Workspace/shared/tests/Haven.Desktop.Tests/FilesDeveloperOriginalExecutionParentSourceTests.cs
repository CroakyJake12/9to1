using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Actual_kernel_saved_pin_retains_held_home_read_and_restored_parent_callback_refuses_own_join()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        var errors = new List<Exception>(); var raw = new List<Task>();
        Task<IDeveloperWorkspaceOriginalExecutionCommitPin>? actual = null;
        IDeveloperWorkspaceOriginalExecutionCommitPin? pin = null; Task? pinClose = null;
        SemaphoreSlim? gate = null; var held = false;
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutionContext? earlier = null; var released = false; var refusals = new List<Exception>();
        var unexpectedCloses = new List<Task>();
        try
        {
            earlier = ExecutionContext.Capture()!;
            await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
            var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture,
                rig.Permission, intent.Steps[^1], token);
            var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
            var source = Assert.IsAssignableFrom<IDeveloperWorkspaceOriginalExecutionScopedBindingSource>(rig.Kernel);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            gate = Assert.IsType<SemaphoreSlim>(rig.Home.GetType().GetField("_gate", flags)!.GetValue(rig.Home));
            await gate.WaitAsync(token); held = true;
            actual = source.AcquireOriginalExecutionPinWithinSourceAsync(binding, callback =>
            {
                if (Volatile.Read(ref released))
                    ExecutionContext.Run(earlier!.CreateCopy(), state =>
                    {
                        var refusal = Assert.Throws<InvalidOperationException>((Action)(() =>
                        { var returned = rig.Kernel.CloseAndDrainOriginalCapturesAsync(); lock (unexpectedCloses) unexpectedCloses.Add(returned); }));
                        lock (refusals) refusals.Add(refusal); callback();
                    }, null);
                else callback();
            }, task =>
            {
                lock (raw) raw.Add(task);
                if (task is Task<HomeStateReadResult> && !task.IsCompleted) enrolled.TrySetResult();
            }, token);
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.False(actual.IsCompleted);
            Assert.Contains(raw, task => task is Task<HomeStateReadResult> && !task.IsCompleted);
            Volatile.Write(ref released, true); gate.Release(); held = false;
            pin = await actual.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.NotEmpty(refusals);
            Assert.True(((IDeveloperWorkspaceOriginalExecutionCommitBindingSource)rig.Kernel).IsIssuedOriginalExecutionPin(binding, pin));
            pin.DemandOriginalExecutionBinding();
            Assert.All(raw, task => Assert.True(task.IsCompletedSuccessfully));
        }
        catch (Exception error) { Capture(null, error); }
        finally
        {
            if (held) gate!.Release();
            if (actual is not null)
                try { var product = await actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); pin ??= product; }
                catch (Exception error) { Capture(actual, error); }
            if (pin is not null)
                try { pinClose = pin.DisposeAsync().AsTask(); } catch (Exception error) { Capture(null, error); }
            if (pinClose is not null) try { await pinClose; } catch (Exception error) { Capture(pinClose, error); }
            Task[] originals; lock (raw) originals = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var task in originals) try { await task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                catch (Exception error) { Capture(task, error); }
            Task[] acquiredCloses; lock (unexpectedCloses) acquiredCloses = unexpectedCloses.ToArray();
            foreach (var returned in acquiredCloses) try { await returned; } catch (Exception error) { Capture(returned, error); }
            Task? close = null;
            try { close = rig.DisposeAsync().AsTask(); } catch (Exception error) { Capture(null, error); }
            if (close is not null) try { await close; } catch (Exception error) { Capture(close, error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Capture(Task? original, Exception error)
        {
            foreach (var cause in original?.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable())
                foreach (var leaf in Causes(cause)) if (!errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }
    [LinuxDirectoryFact]
    public async Task Parent_refusal_after_actual_acquisition_cannot_strand_private_stream_or_pin_handles()
    {
        foreach (var wrappedFilesPin in new[] { false, true })
        {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        var refusal = new IOException("controlled parent refuses owed cleanup");
        var raw = new List<Task>(); var expected = new List<Exception>(); var errors = new List<Exception>();
        Task<IDeveloperWorkspaceOriginalExecutionCommitPin>? actual = null;
        var reject = false; var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        try
        {
            await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
            var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture,
                rig.Permission, intent.Steps[^1], token);
            var binding = await rig.Effector.AcquireOriginalExecutionBindingAsync(saved, Reference(intent), token);
            var source = Assert.IsAssignableFrom<IDeveloperWorkspaceOriginalExecutionScopedBindingSource>(wrappedFilesPin ? rig.Effector : rig.Kernel);
            actual = source.AcquireOriginalExecutionPinWithinSourceAsync(binding, callback =>
            {
                var wrapped = ((System.Collections.IEnumerable)rig.Effector.GetType().GetField("_originalExecutionPins", flags)!.GetValue(rig.Effector)!).Cast<object>().Any();
                if (Volatile.Read(ref reject) || wrappedFilesPin && wrapped) throw refusal;
                callback();
            }, task =>
            {
                lock (raw) raw.Add(task);
                // A real descriptor-backed read has been acquired and enrolled. Subsequent
                // productive scopes refuse before invoking their supplied factory.
                if (!wrappedFilesPin && task is Task<int>) Volatile.Write(ref reject, true);
            }, token);
            Exception? failure = null;
            try { _ = await actual.WaitAsync(TimeSpan.FromSeconds(5), token); }
            catch (Exception error) { failure = error; }
            Assert.NotNull(failure);
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            var leaves = actual.Exception!.InnerExceptions.SelectMany(Causes).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            Assert.Contains(leaves, cause => ReferenceEquals(cause, refusal));
            Assert.All(leaves, cause => Assert.True(ReferenceEquals(cause, refusal) || cause is InvalidOperationException &&
                cause.Message is "Original kernel parent scope did not invoke its callback." or "Original source scope did not invoke its callback."));
            expected.AddRange(leaves);
            var pins = ((System.Collections.IEnumerable)rig.Kernel.GetType().GetField("_originalSavedRootPins", flags)!.GetValue(rig.Kernel)!).Cast<object>().ToArray();
            Assert.NotEmpty(pins);
            foreach (var pin in pins)
            {
                foreach (var name in new[] { "MetadataHandle", "RegistrationHandle", "WorkingRoot" })
                    if (pin.GetType().GetField(name, flags)!.GetValue(pin) is Microsoft.Win32.SafeHandles.SafeFileHandle handle)
                        Assert.True(handle.IsClosed);
                foreach (var name in new[] { "MetadataRoot", "FilesRoot" })
                    if (pin.GetType().GetField(name, flags)!.GetValue(pin) is { } lease)
                        Assert.True(Assert.IsType<Microsoft.Win32.SafeHandles.SafeFileHandle>(lease.GetType().GetField("_rootHandle", flags)!.GetValue(lease)).IsClosed);
                var original = pin.GetType().GetField("Original", flags)!.GetValue(pin)!;
                var originals = ((System.Collections.IEnumerable)original.GetType().GetField("Sources", flags)!.GetValue(original)!).Cast<Task>().ToArray();
                Assert.All(originals, task => Assert.True(task.IsCompleted));
                Assert.Contains(originals, task => task.IsCompletedSuccessfully);
            }
        }
        catch (Exception error) { Capture(null, error); }
        finally
        {
            if (actual is not null) try { _ = await actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                catch (Exception error) { Capture(actual, error); }
            Task[] tasks; lock (raw) tasks = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var task in tasks) try { await task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                catch (Exception error) { Capture(task, error); }
            Task? close = null;
            try { close = rig.DisposeAsync().AsTask(); } catch (Exception error) { Capture(null, error); }
            if (close is not null) try { await close; } catch (Exception error) { Capture(close, error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Capture(Task? original, Exception error)
        {
            foreach (var cause in original?.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable())
                foreach (var leaf in Causes(cause))
                    if (!expected.Any(value => ReferenceEquals(value, leaf)) && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
        }
    }

}
