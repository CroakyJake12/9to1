using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeLocalOperationLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-home-operation-" + Guid.NewGuid().ToString("N"));
    private readonly Principal _principal = new();
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    public HomeLocalOperationLeaseTests()
    {
        Directory.CreateDirectory(_root);
        _store = new(Path.Combine(_root, "home.json"));
        _profiles = new(_store, _principal);
    }
    private static HomeCoreStateRecord Record => new("operation-test", "operation-test", 1,
        HomeDataScope.DeviceLocal, HomeRecordAuthority.LocalCanonical, 0, JsonSerializer.SerializeToElement(new { value = "committed" }));
    [Fact]
    public async Task Actual_same_path_writer_and_exclusive_process_lock_stay_held_until_disposal()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        var lease = Assert.IsAssignableFrom<IHomeLocalOperationLease>(await _store.AcquireLocalOperationLeaseAsync(_profiles, actor));
        try
        {
            Assert.True(await lease.IsCurrentAsync());
            var writer = new FileHomeCoreStateStore(Path.Combine(_root, "home.json")).WriteAsync(Record, 0);
            Assert.False(writer.IsCompleted);
            Assert.Throws<IOException>(() => { using var competing = new FileStream(Path.Combine(_root, "home.json.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            await lease.DisposeAsync();
            Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
            Assert.False(await lease.IsCurrentAsync());
            await lease.DisposeAsync();
            Assert.Single((await _store.ReadAsync()).State!.Records, item => item.RecordId == Record.RecordId);
        }
        finally { await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task Same_path_different_store_instance_and_foreign_principal_never_issue_a_lease()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var other = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"));
        Assert.Null(await other.AcquireLocalOperationLeaseAsync(_profiles, actor));
        _principal.Value = "different-actual-fixture-principal";
        Assert.Null(await _store.AcquireLocalOperationLeaseAsync(_profiles, actor));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.True((await _store.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
    }
    [Fact]
    public async Task Actual_profile_UUID_replacement_and_remote_actor_fields_deny_original_lease()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        Assert.Null(await _store.AcquireLocalOperationLeaseAsync(_profiles, actor with { AccountId = Guid.NewGuid() }));
        var state = (await _store.ReadAsync()).State!;
        var record = Assert.Single(state.Records, item => item.RecordId == "home.local-profile");
        var profile = record.Payload.Deserialize<HomeLocalProfile>()!;
        Assert.True((await _store.WriteAsync(record with { Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) }, record.Revision)).IsSuccess);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        Assert.Null(await _store.AcquireLocalOperationLeaseAsync(_profiles, actor));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.True((await _store.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
    }
    [Fact]
    public async Task Dispose_revokes_suspended_actual_guard_before_completion_and_serializes_release()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        var lease = (await _store.AcquireLocalOperationLeaseAsync(_profiles, actor))!;
        try
        {
            _principal.HoldNext = true;
            var check = lease.IsCurrentAsync().AsTask();
            await _principal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var close = lease.DisposeAsync().AsTask();
            var writer = _store.WriteAsync(Record, 0);
            Assert.False(close.IsCompleted); Assert.False(writer.IsCompleted);
            _principal.Release.TrySetResult();
            Assert.False(await check.WaitAsync(TimeSpan.FromSeconds(10)));
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
        }
        finally { _principal.Release.TrySetResult(); await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task Cancelled_acquisition_guard_releases_both_actual_locks_without_publication()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        using var cancelled = new CancellationTokenSource();
        _principal.HoldNext = true;
        var acquiring = _store.AcquireLocalOperationLeaseAsync(_profiles, actor, cancelled.Token).AsTask();
        await _principal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquiring);
        _principal.Release.TrySetResult();
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.True((await _store.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
        await using var recovered = (await _store.AcquireLocalOperationLeaseAsync(_profiles, actor))!;
        Assert.True(await recovered.IsCurrentAsync());
    }
    [Fact]
    public async Task Cancellation_while_waiting_for_original_gate_does_not_release_someone_elses_lease()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        await using var lease = (await _store.AcquireLocalOperationLeaseAsync(_profiles, actor))!;
        using var cancelled = new CancellationTokenSource();
        var pending = _store.AcquireLocalOperationLeaseAsync(_profiles, actor, cancelled.Token).AsTask();
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(await lease.IsCurrentAsync());
        var writer = _store.WriteAsync(Record, 0); Assert.False(writer.IsCompleted);
        await lease.DisposeAsync(); Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observed_out_of_protocol_profile_replacement_never_adopts_new_root(bool duringPrincipalRead)
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        var original = (await _store.ReadAsync()).State!;
        var identity = Assert.Single(original.Records, item => item.RecordId == "home.local-profile");
        var changed = identity with { Payload = JsonSerializer.SerializeToElement(identity.Payload.Deserialize<HomeLocalProfile>()! with { ProfileId = Guid.NewGuid() }) };
        var replacement = original with { Records = original.Records.Select(item => item.RecordId == identity.RecordId ? changed : item).ToArray() };
        await using var lease = (await _store.AcquireLocalOperationLeaseAsync(_profiles, actor))!;
        Task<bool>? check = null;
        if (duringPrincipalRead)
        {
            _principal.HoldNext = true;
            check = lease.IsCurrentAsync().AsTask();
            await _principal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        // Deliberate out-of-protocol writer: no .lock/gate. This is NOT a supported atomic writer guarantee.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(replacement);
        await File.WriteAllBytesAsync(Path.Combine(_root, "home.json"), bytes);
        _principal.Release.TrySetResult();
        Assert.False(await (check ?? lease.IsCurrentAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        await lease.DisposeAsync();
        Assert.Null(await _store.AcquireLocalOperationLeaseAsync(_profiles, actor));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }

    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Value = "actual-controlled-fixture-principal";
        public bool HoldNext;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HoldNext) { HoldNext = false; Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return Value;
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
