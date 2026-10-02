using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeActualPersistedWriteObserverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-home-persisted-observer-" + Guid.NewGuid().ToString("N"));
    private HomeCoreStateRecord Record(string id = "actual.fixture") => new(id, "actual.fixture", 1,
        HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 1, JsonSerializer.SerializeToElement(new { value = "original" }));

    [Fact]
    public async Task Observer_reads_actual_persisted_record_without_FileHome_lock_reentry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        FileHomeCoreStateStore? actual = null;
        var observer = new Observer(async (id, revision, ct) =>
        {
            var read = await actual!.ReadAsync(ct);
            Assert.True(read.IsSuccess);
            var persisted = Assert.Single(read.State!.Records, item => item.RecordId == id);
            Assert.Equal(revision, persisted.Revision); Assert.Equal("original", persisted.Payload.GetProperty("value").GetString());
        });
        actual = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"), observer);
        Assert.True((await actual.WriteAsync(Record(), 0, timeout.Token)).IsSuccess);
        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task Lost_actual_write_acknowledgement_preserves_bytes_and_stale_retry_does_not_republish()
    {
        var failure = new IOException("Controlled acknowledgement loss after genuine publication");
        var observer = new Observer((_, _, _) => Task.FromException(failure));
        var actual = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"), observer);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => actual.WriteAsync(Record(), 0)));
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var read = await actual.ReadAsync(); Assert.Equal(1, Assert.Single(read.State!.Records).Revision);
        var retry = await actual.WriteAsync(Record(), 0);
        Assert.False(retry.IsSuccess); Assert.Equal(HomeCoreErrorCode.HomeStateConflict, retry.Failure!.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task Guard_refusal_and_CAS_conflict_never_notify_observer()
    {
        var observer = new Observer((_, _, _) => Task.CompletedTask);
        var actual = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"), observer);
        var actor = new AuthenticatedResourceActor("fixture", "fixture-profile", null, null, "fixture-session");
        var denied = await actual.WriteGuardedAsync(Record("refused"), 0, actor, new Deny());
        Assert.False(denied.IsSuccess); Assert.Equal(0, observer.Calls);
        Assert.Empty((await actual.ReadAsync()).State!.Records);
        Assert.True((await actual.WriteAsync(Record(), 0)).IsSuccess);
        Assert.False((await actual.WriteAsync(Record(), 0)).IsSuccess);
        Assert.Equal(1, observer.Calls);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Observer(Func<string, long, CancellationToken, Task> callback) : IHomePersistedWriteObserver
    {
        public int Calls;
        public Task OnPersistedWriteAsync(string id, long revision, CancellationToken ct)
        { Calls++; return callback(id, revision, ct); }
    }
    private sealed class Deny : IHomeStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(false);
    }
}
