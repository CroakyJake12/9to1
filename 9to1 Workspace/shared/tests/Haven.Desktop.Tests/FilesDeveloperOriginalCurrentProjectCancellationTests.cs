using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Actual_cancelled_current_descriptor_read_keeps_raw_cancelled_status_and_closes_all_native_handles()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        using var canceled = new CancellationTokenSource();
        FilesDeveloperOriginalCurrentProjectSelection? selected = null;
        IDeveloperOriginalCurrentProjectNativeSource? native = null;
        Task<IDeveloperOriginalCurrentProjectNativeRead>? actual = null; Task<int>? fileRead = null;
        var raw = new List<Task>(); var errors = new List<Exception>(); var witnessed = false;
        try
        {
            await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
            await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
            var container = CurrentProjectContainer(rig.SourcePath); var conversation = CurrentProjectConversation(container);
            selected = new(rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>(), rig.WorkspaceStore, new CurrentProjectContainers(container));
            var selection = await selected.SelectOriginalWithinSourceAsync(conversation, container,
                JsonSerializer.Serialize(Reference(intent)), null, null, body => body(), task => { lock (raw) raw.Add(task); }, token);
            var issuer = new CurrentProjectReads(selection);
            native = new WorkspaceToolService().CreateOriginalCurrentProjectNativeSource(() => selected, issuer, rig.WorkspaceStore);
            var sameNative = native;
            void Scope(Action body)
            {
                // Cancel only after the actual saved-document, registration and working-root
                // descriptors exist. The next real FileStream.ReadAsync owns cancellation;
                // the source callback itself neither throws nor manufactures a Task.
                if (!Volatile.Read(ref witnessed) && CurrentProjectHandles(sameNative).Count(handle => !handle.IsClosed) == 3)
                { Volatile.Write(ref witnessed, true); canceled.Cancel(); }
                body();
            }
            void Retain(Task task)
            {
                lock (raw)
                {
                    raw.Add(task);
                    if (Volatile.Read(ref witnessed) && task is Task<int> { IsCanceled: true } originalRead)
                        fileRead ??= originalRead;
                }
            }
            actual = native.CaptureOriginalWithinSourceAsync(selection, issuer.Actual, Scope, Retain, canceled.Token);
            var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            Assert.True(Volatile.Read(ref witnessed));
            Assert.True(actual.IsCanceled); Assert.False(actual.IsFaulted);
            Assert.Null(actual.Exception);
            Assert.NotNull(fileRead); Assert.True(fileRead.IsCanceled); Assert.False(fileRead.IsFaulted);
            Assert.Equal(canceled.Token, caught.CancellationToken);
            Assert.True(IsOriginalCancellation(caught));
            var handles = CurrentProjectHandles(native).ToArray();
            Assert.NotEmpty(handles); Assert.All(handles, handle => Assert.True(handle.IsClosed));
            Task[] originals; lock (raw) originals = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            Assert.Contains(originals, task => ReferenceEquals(task, fileRead));
            Assert.Contains(originals, task => ReferenceEquals(task, actual));
            Assert.All(originals, task => Assert.True(task.IsCompleted));
            Assert.DoesNotContain(originals, task => task.IsFaulted);
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            // Business cancellation never cancels owning cleanup. The same actual capture,
            // both source owners, every retained raw Task and the real Rig close are joined.
            if (actual is not null) try { await actual; } catch (Exception error) { Keep(actual, error); }
            await CloseCurrentProjectOwners(native, selected, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        bool IsOriginalCancellation(Exception cause)
        {
            if (cause is not TaskCanceledException { Task: { IsCanceled: true } sameTask } original
                || original.CancellationToken != canceled.Token) return false;
            lock (raw) return raw.Any(value => ReferenceEquals(value, sameTask));
        }
        void Keep(Task? task, Exception error)
        {
            foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error))
                if (!IsOriginalCancellation(cause) && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
        }
    }
}
