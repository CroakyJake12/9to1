using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32.SafeHandles;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Fresh_current_project_reads_actual_document_and_root_without_borrowing_old_setup_binding()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
        IDeveloperOriginalCurrentProjectNativeRead? read = null; Task<IDeveloperOriginalCurrentProjectNativeRead>? capture = null;
        Task? readClose = null; var raw = new List<Task>(); var errors = new List<Exception>();
        try
        {
            await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
            await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture, rig.Permission, intent.Steps[^1], token);
            var container = CurrentProjectContainer(rig.SourcePath); var conversation = CurrentProjectConversation(container);
            selected = new(rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>(), rig.WorkspaceStore, new CurrentProjectContainers(container));
            var selection = await selected.SelectOriginalWithinSourceAsync(conversation, container,
                JsonSerializer.Serialize(Reference(intent)), null, null, body => body(), task => { lock (raw) raw.Add(task); }, token);
            var descriptor = selected.GetOriginalDescriptor(selection); var issuer = new CurrentProjectReads(selection);
            native = new WorkspaceToolService().CreateOriginalCurrentProjectNativeSource(() => selected, issuer, rig.WorkspaceStore);
            var sameNative = native; var before = ExecutionContext.Capture()!;
            void Scope(Action body)
            {
                ExecutionContext.Run(before.CreateCopy(), _ => Assert.Throws<InvalidOperationException>((Action)(() =>
                    { _ = sameNative.CloseAndDrainOriginalAsync(); })), null);
                body();
            }
            capture = native.CaptureOriginalWithinSourceAsync(selection, issuer.Actual, Scope, task => { lock (raw) raw.Add(task); }, token);
            read = await capture;
            Assert.True(native.IsIssuedOriginalRead(selection, capture, read));
            Assert.True(native.IsOwnedOriginalRead(selection, capture, read));
            Assert.Contains(raw, task => ReferenceEquals(task, capture));
            Assert.Equal(rig.Prepared.Intent.WorkspaceId, descriptor.WorkspaceId);
            Assert.Equal(Path.GetDirectoryName(rig.SourcePath), descriptor.RegisteredProjectRoot);
            var path = Path.Combine(rig.WorkspaceStore.OriginalWorkspaceMetadataDirectory, intent.WorkspaceId.ToString("N") + ".json");
            Assert.Equal(File.ReadAllText(path), read.OriginalWorkspaceDocument);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), read.OriginalWorkspaceDocumentSha256);
            read.DemandOriginalExecutionBinding();
            // Native-only Demand notices same-path document overwrite; no Home/profile
            // callback or reacquisition is needed under an eventual identity commit pin.
            var bytes = File.ReadAllBytes(path);
            try { File.WriteAllText(path, "{}"); Assert.Throws<IOException>((Action)read.DemandOriginalExecutionBinding); }
            finally { File.WriteAllBytes(path, bytes); }
            readClose = read.DisposeAsync().AsTask(); await readClose;
            Assert.False(native.IsIssuedOriginalRead(selection, capture, read));
            Assert.True(native.IsOwnedOriginalRead(selection, capture, read));
            Assert.True(native.IsClosedOriginalRead(selection, capture, read, readClose));
            var restored = await selected.SelectOriginalRestorationWithinSourceAsync(conversation, container.Id,
                Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(container,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant(),
                JsonSerializer.Serialize(Reference(intent)), read.OriginalWorkspaceDocumentSha256,
                read.OriginalRegisteredRootFingerprint, body => body(), task => { lock (raw) raw.Add(task); }, token);
            Assert.NotSame(selection, restored);
            Assert.Equal(container, selected.GetOriginalDescriptor(restored).OriginalContainer);
            Assert.Equal(read.OriginalWorkspaceDocumentSha256, selected.GetOriginalDescriptor(restored).ExpectedWorkspaceDocumentSha256);
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            if (read is not null && readClose is null) try { readClose = read.DisposeAsync().AsTask(); } catch (Exception error) { Keep(null, error); }
            foreach (var task in new Task?[] { capture, readClose }) if (task is not null) try { await task; } catch (Exception error) { Keep(task, error); }
            await CloseCurrentProjectOwners(native, selected, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Keep(Task? task, Exception error)
        { foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error)) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
    }

    [LinuxDirectoryFact]
    public async Task Parent_refusal_after_real_current_descriptor_acquisition_cannot_skip_fixed_owned_native_cleanup()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        FilesDeveloperOriginalCurrentProjectSelection? selected = null; IDeveloperOriginalCurrentProjectNativeSource? native = null;
        Task<IDeveloperOriginalCurrentProjectNativeRead>? actual = null; var raw = new List<Task>();
        var refusal = new IOException("the same actual parent refused after current project descriptor acquisition");
        var errors = new List<Exception>(); var refusing = false; var witnessed = false;
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
            void Scope(Action body) { if (Volatile.Read(ref refusing)) throw refusal; body(); }
            void Retain(Task task)
            {
                lock (raw) raw.Add(task);
                if (CurrentProjectHandles(native).Any(handle => !handle.IsClosed)) { Volatile.Write(ref witnessed, true); Volatile.Write(ref refusing, true); }
            }
            actual = native.CaptureOriginalWithinSourceAsync(selection, issuer.Actual, Scope, Retain, token);
            var caught = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.True(Volatile.Read(ref witnessed));
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            Assert.Same(refusal, Assert.Single(CurrentProjectLeaves(actual.Exception ?? caught)));
            var handles = CurrentProjectHandles(native).ToArray();
            Assert.NotEmpty(handles);
            Assert.All(handles, handle => Assert.True(handle.IsClosed));
            Assert.All(raw, task => Assert.True(task.IsCompleted));
        }
        catch (Exception error) { Keep(null, error); }
        finally
        {
            if (actual is not null) try { await actual; } catch (Exception error) { Keep(actual, error); }
            await CloseCurrentProjectOwners(native, selected, rig, raw, Keep);
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Keep(Task? task, Exception error)
        {
            foreach (var cause in CurrentProjectLeaves(task?.Exception ?? error))
                if (!ReferenceEquals(cause, refusal) && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
        }
    }
    private static ContainerDefinition CurrentProjectContainer(string file)
        => new(Guid.NewGuid(), HavenMode.Tasks, "actual selected project container", Path.GetDirectoryName(file),
            "actual container context", "actual container instructions", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static Conversation CurrentProjectConversation(ContainerDefinition container)
        => new(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task, "selected project", container.Id, null,
            false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static async Task CloseCurrentProjectOwners(IDeveloperOriginalCurrentProjectNativeSource? native,
        FilesDeveloperOriginalCurrentProjectSelection? selected, Rig rig, List<Task> raw, Action<Task?, Exception> keep)
    {
        // Request both owner drains before waiting; neither owner disposes borrowed Home.
        native?.RequestOriginalRetirement(); selected?.RequestOriginalCurrentProjectRetirement();
        var closes = new List<Task>();
        if (native is not null) try { closes.Add(native.CloseAndDrainOriginalAsync()); } catch (Exception error) { keep(null, error); }
        if (selected is not null) try { closes.Add(selected.CloseAndDrainOriginalCurrentProjectsAsync()); } catch (Exception error) { keep(null, error); }
        foreach (var close in closes) try { await close; } catch (Exception error) { keep(close, error); }
        Task[] originals; lock (raw) originals = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var task in originals) try { await task; } catch (Exception error) { keep(task, error); }
        Task? actual = null; try { actual = rig.DisposeAsync().AsTask(); } catch (Exception error) { keep(null, error); }
        if (actual is not null) try { await actual; } catch (Exception error) { keep(actual, error); }
    }
    private static IEnumerable<SafeFileHandle> CurrentProjectHandles(IDeveloperOriginalCurrentProjectNativeSource native)
    {
        // Inspection-only exact private acquired-handle inventory, never an issuer/grant.
        var reads = (System.Collections.IEnumerable)native.GetType()
            .GetField("_reads", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(native)!;
        foreach (var read in reads)
            foreach (var name in new[] { "MetadataHandle", "RegistrationHandle", "WorkingRoot" })
                if (read!.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(read) is SafeFileHandle handle) yield return handle;
    }
    private static IEnumerable<Exception> CurrentProjectLeaves(Exception error)
        => error is AggregateException { InnerExceptions.Count: > 0 } group ? group.InnerExceptions.SelectMany(CurrentProjectLeaves) : new[] { error };
    // Synthetic configured READ issuer/repository for kernel ordering controls only. The
    // actual Home current-project source must issue real manual READ; this rig does not.
    private sealed class CurrentProjectReads(IDeveloperOriginalCurrentProjectSelection selection) : IDeveloperOriginalCurrentProjectReadAdmissionSource
    {
        internal readonly IDeveloperProjectOriginalReadAdmission Actual = new CurrentRead();
        public bool IsIssuedOriginalCurrentProjectRead(IDeveloperOriginalCurrentProjectSelection same, IDeveloperProjectOriginalReadAdmission read)
            => ReferenceEquals(selection, same) && ReferenceEquals(Actual, read);
        public Task ValidateOriginalCurrentProjectReadWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection same,
            IDeveloperProjectOriginalReadAdmission read, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            scope(() => { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalCurrentProjectRead(same, read)) throw new UnauthorizedAccessException(); });
            return Task.CompletedTask;
        }
        private sealed class CurrentRead : IDeveloperProjectOriginalReadAdmission
        {
            public Task RevalidateOriginalAsync(CancellationToken token) => Task.CompletedTask;
            public T RunOriginalRead<T>(Func<T> original, CancellationToken token) { token.ThrowIfCancellationRequested(); return original(); }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class CurrentProjectContainers(ContainerDefinition actual) : IContainerRepository
    {
        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ContainerDefinition>>(mode == actual.Mode ? [actual] : []); }
        public Task UpsertAsync(ContainerDefinition item, CancellationToken token) => throw new NotSupportedException();
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition item, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertLessonAsync(Lesson item, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteLessonAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
}
