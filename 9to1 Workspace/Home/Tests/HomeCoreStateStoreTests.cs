using System.Diagnostics;
using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeCoreStateStoreTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    { public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires Linux file-lock semantics."; } }

    [Fact]
    public async Task Store_creates_versioned_records_and_rejects_stale_revision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileHomeCoreStateStore(path);
            var payload = JsonDocument.Parse("{\"mode\":\"dark\"}").RootElement.Clone();
            var record = new HomeCoreStateRecord("settings", "settings", 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 0, payload);
            var first = await store.WriteAsync(record, 0);
            Assert.True(first.IsSuccess);
            Assert.Equal(1, first.State!.Records.Single().Revision);
            var conflict = await new FileHomeCoreStateStore(path).WriteAsync(record, 0);
            Assert.Equal(HomeCoreErrorCode.HomeStateConflict, conflict.Failure!.Code);
            Assert.Equal("dark", (await store.ReadAsync()).State!.Records.Single().Payload.GetProperty("mode").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [Fact]
    public async Task Independent_store_instances_serialize_concurrent_writes_and_reopen_without_lost_records()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        try
        {
            var firstStore = new FileHomeCoreStateStore(path);
            var secondStore = new FileHomeCoreStateStore(path);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            static HomeCoreStateRecord Record(string id, string value)
            {
                using var document = JsonDocument.Parse($"{{\"value\":\"{value}\"}}");
                return new(id, "settings", 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                    0, document.RootElement.Clone());
            }

            async Task<HomeStateWriteResult> WriteAfterStart(FileHomeCoreStateStore store, string id, string value)
            {
                await start.Task;
                return await store.WriteAsync(Record(id, value), 0);
            }
            var firstWrite = WriteAfterStart(firstStore, "first", "one");
            var secondWrite = WriteAfterStart(secondStore, "second", "two");
            start.SetResult();
            var writes = await Task.WhenAll(firstWrite, secondWrite);

            Assert.All(writes, result => Assert.True(result.IsSuccess, result.Failure?.Message));
            var reopened = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.True(reopened.IsSuccess);
            Assert.Equal(2, reopened.State!.Revision);
            Assert.Equal(new[] { "first", "second" }, reopened.State.Records.Select(record => record.RecordId).ToArray());
            Assert.Equal("one", reopened.State.Records.Single(record => record.RecordId == "first")
                .Payload.GetProperty("value").GetString());
            Assert.Equal("two", reopened.State.Records.Single(record => record.RecordId == "second")
                .Payload.GetProperty("value").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [Fact]
    public async Task Independent_store_instances_allow_only_one_concurrent_writer_at_the_same_record_revision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        try
        {
            var first = new FileHomeCoreStateStore(path);
            var second = new FileHomeCoreStateStore(path);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            static HomeCoreStateRecord Record(string value)
            {
                using var document = JsonDocument.Parse($"{{\"value\":\"{value}\"}}");
                return new("settings", "settings", 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                    0, document.RootElement.Clone());
            }

            async Task<HomeStateWriteResult> WriteAfterStart(FileHomeCoreStateStore store, string value)
            {
                await start.Task;
                return await store.WriteAsync(Record(value), 0);
            }
            var firstWrite = WriteAfterStart(first, "first");
            var secondWrite = WriteAfterStart(second, "second");
            start.SetResult();
            var writes = await Task.WhenAll(firstWrite, secondWrite);

            Assert.Single(writes, result => result.IsSuccess);
            var conflict = Assert.Single(writes, result => !result.IsSuccess);
            Assert.Equal(HomeCoreErrorCode.HomeStateConflict, conflict.Failure!.Code);
            var reopened = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.True(reopened.IsSuccess);
            Assert.Equal(1, reopened.State!.Revision);
            Assert.Equal(1, reopened.State.Records.Single().Revision);
            Assert.Contains(reopened.State.Records.Single().Payload.GetProperty("value").GetString(), new[] { "first", "second" });
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [Fact]
    public async Task Lost_persisted_write_acknowledgement_does_not_hide_commit_or_make_retry_idempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        var acknowledgementFailure = new IOException("Controlled acknowledgement loss after durable publication.");
        try
        {
            var observer = new ThrowingPersistedWriteObserver(acknowledgementFailure);
            var store = new FileHomeCoreStateStore(path, observer);
            using var document = JsonDocument.Parse("{\"mode\":\"dark\"}");
            var record = new HomeCoreStateRecord("settings", "settings", 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 0, document.RootElement.Clone());

            var observed = await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(record, 0));
            Assert.Same(acknowledgementFailure, observed);
            Assert.Equal(("settings", 1L), observer.Published!.Value);

            var reopened = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.True(reopened.IsSuccess);
            Assert.Equal("dark", reopened.State!.Records.Single().Payload.GetProperty("mode").GetString());
            var retry = await new FileHomeCoreStateStore(path).WriteAsync(record, 0);
            Assert.Equal(HomeCoreErrorCode.HomeStateConflict, retry.Failure!.Code);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    private sealed class ThrowingPersistedWriteObserver(IOException failure) : IHomePersistedWriteObserver
    {
        internal (string Id, long Revision)? Published { get; private set; }
        public Task OnPersistedWriteAsync(string recordId, long actualRecordRevision, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published = (recordId, actualRecordRevision);
            return Task.FromException(failure);
        }
    }

    [Fact]
    public async Task Store_reports_corruption_without_replacing_the_original_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        const string original = "{ not valid state";
        try
        {
            await File.WriteAllTextAsync(path, original);
            var result = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, result.Failure!.Code);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Store_does_not_replace_a_newer_incompatible_state_schema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        var original = JsonSerializer.Serialize(new HomeCoreStoredState(FileHomeCoreStateStore.CurrentSchemaVersion + 1, 3, []));
        try
        {
            await File.WriteAllTextAsync(path, original);
            var store = new FileHomeCoreStateStore(path);

            var read = await store.ReadAsync();

            Assert.Equal(HomeCoreErrorCode.HomeStateIncompatible, read.Failure!.Code);
            using var document = JsonDocument.Parse("{\"mode\":\"light\"}");
            var write = await store.WriteAsync(new HomeCoreStateRecord("settings", "settings", 1,
                HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 0, document.RootElement.Clone()), 0);
            Assert.Equal(HomeCoreErrorCode.HomeStateIncompatible, write.Failure!.Code);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [Fact]
    public async Task Store_rejects_a_record_revision_ahead_of_its_containing_state_revision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        using var document = JsonDocument.Parse("{\"mode\":\"dark\"}");
        var record = new HomeCoreStateRecord("settings", "settings", 1, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, 1, document.RootElement.Clone());
        var inconsistent = new HomeCoreStoredState(FileHomeCoreStateStore.CurrentSchemaVersion, 0, [record]);
        var original = JsonSerializer.Serialize(inconsistent);
        try
        {
            await File.WriteAllTextAsync(path, original);
            Assert.False(FileHomeCoreStateStore.IsOriginalProtectedStateValid(inconsistent));
            var store = new FileHomeCoreStateStore(path);

            var read = await store.ReadAsync();

            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, read.Failure!.Code);
            var attemptedWrite = await store.WriteAsync(record with { Revision = 0 }, 0);
            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, attemptedWrite.Failure!.Code);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [Fact]
    public async Task Store_returns_empty_initial_state_only_when_the_state_file_is_absent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        try
        {
            var result = await new FileHomeCoreStateStore(path).ReadAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.State!.Revision);
            Assert.Empty(result.State.Records);
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path + ".lock")) File.Delete(path + ".lock"); }
    }

    [Fact]
    public async Task Cancellation_before_write_admission_publishes_nothing_and_later_writer_can_commit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileHomeCoreStateStore(path);
            using var document = JsonDocument.Parse("{\"value\":\"after-cancel\"}");
            var record = new HomeCoreStateRecord("settings", "settings", 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 0, document.RootElement.Clone());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(record, 0, canceled.Token));
            var afterCancellation = await store.ReadAsync();
            Assert.True(afterCancellation.IsSuccess);
            Assert.Empty(afterCancellation.State!.Records);

            var laterWrite = await store.WriteAsync(record, 0);
            Assert.True(laterWrite.IsSuccess, laterWrite.Failure?.Message);
            Assert.Equal(1, laterWrite.State!.Records.Single().Revision);
            var reopened = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.Equal(1, reopened.State!.Revision);
            Assert.Equal(1, reopened.State.Records.Single().Revision);
            Assert.Equal("after-cancel", reopened.State.Records.Single().Payload.GetProperty("value").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_process_lock_releases_store_gate_without_publication()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileHomeCoreStateStore(path);
            using var document = JsonDocument.Parse("{\"value\":\"after-lock-cancel\"}");
            var record = new HomeCoreStateRecord("settings", "settings", 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 0, document.RootElement.Clone());
            using (new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var canceled = new CancellationTokenSource())
            {
                var waitingWrite = store.WriteAsync(record, 0, canceled.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(120));
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingWrite);
                Assert.False(File.Exists(path));
            }

            var laterWrite = await store.WriteAsync(record, 0);
            Assert.True(laterWrite.IsSuccess, laterWrite.Failure?.Message);
            var reopened = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.True(reopened.IsSuccess);
            Assert.Equal(1, reopened.State!.Revision);
            Assert.Equal("after-lock-cancel", reopened.State.Records.Single().Payload.GetProperty("value").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [LinuxFact]
    public async Task Independent_process_holding_state_lock_prevents_publication_until_released()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        Process? holder = null;
        Exception? primaryFailure = null;
        try
        {
            var store = new FileHomeCoreStateStore(path);
            var initial = await store.ReadAsync();
            Assert.True(initial.IsSuccess);
            var start = new ProcessStartInfo("python3")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import fcntl,sys; lock_file=open(sys.argv[1],'a+b'); fcntl.flock(lock_file,fcntl.LOCK_EX); print('READY',flush=True); sys.stdin.readline(); lock_file.close()");
            start.ArgumentList.Add(path + ".lock");
            holder = Process.Start(start) ?? throw new InvalidOperationException("Could not start the independent lock holder.");
            Assert.Equal("READY", await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));

            using var document = JsonDocument.Parse("{\"value\":\"after-process-lock\"}");
            var record = new HomeCoreStateRecord("settings", "settings", 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 0, document.RootElement.Clone());
            using var canceled = new CancellationTokenSource();
            var waitingWrite = store.WriteAsync(record, 0, canceled.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(120));
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingWrite);
            Assert.False(File.Exists(path));

            await holder.StandardInput.WriteLineAsync("release");
            await holder.StandardInput.FlushAsync();
            await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, holder.ExitCode);
            holder.Dispose(); holder = null;

            var laterWrite = await store.WriteAsync(record, 0);
            Assert.True(laterWrite.IsSuccess, laterWrite.Failure?.Message);
            var reopened = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.True(reopened.IsSuccess);
            Assert.Equal(1, reopened.State!.Revision);
            Assert.Equal("after-process-lock", reopened.State!.Records.Single().Payload.GetProperty("value").GetString());
        }
        catch (Exception cause) { primaryFailure = cause; }

        List<Exception> failures = [];
        if (holder is not null)
        {
            try { if (!holder.HasExited) holder.Kill(entireProcessTree: true); }
            catch (Exception cause) { failures.Add(cause); }
            try { await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception cause) { failures.Add(cause); }
            try { holder.Dispose(); }
            catch (Exception cause) { failures.Add(cause); }
        }
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
        catch (Exception cause) { failures.Add(cause); }
        if (primaryFailure is not null) failures.Insert(0, primaryFailure);
        if (failures.Count != 0)
            throw new AggregateException("Cross-process state-lock regression or independent cleanup failed.", failures);
    }

    [Fact]
    public async Task Store_does_not_treat_a_directory_at_the_state_path_as_an_empty_new_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        var sentinel = Path.Combine(path, "keep.txt");
        const string original = "preserve existing path contents";
        try
        {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(sentinel, original);

            var result = await new FileHomeCoreStateStore(path).ReadAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, result.Failure!.Code);
            using var document = JsonDocument.Parse("{\"mode\":\"light\"}");
            var attemptedWrite = await new FileHomeCoreStateStore(path).WriteAsync(new HomeCoreStateRecord(
                "settings", "settings", 1, HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical,
                0, document.RootElement.Clone()), 0);
            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, attemptedWrite.Failure!.Code);
            Assert.Equal(original, await File.ReadAllTextAsync(sentinel));
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }

    [LinuxFact]
    public async Task Store_does_not_replace_a_dangling_state_symlink_as_a_new_empty_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        var missingTarget = path + ".missing";
        try
        {
            File.CreateSymbolicLink(path, missingTarget);
            var store = new FileHomeCoreStateStore(path);

            var read = await store.ReadAsync();

            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, read.Failure!.Code);
            using var document = JsonDocument.Parse("{\"mode\":\"dark\"}");
            var write = await store.WriteAsync(new HomeCoreStateRecord("settings", "settings", 1,
                HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 0, document.RootElement.Clone()), 0);
            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, write.Failure!.Code);
            Assert.Equal(missingTarget, new FileInfo(path).LinkTarget);
            Assert.False(File.Exists(missingTarget));
        }
        finally
        {
            if (File.Exists(path) || new FileInfo(path).LinkTarget is not null) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
        }
    }
}
