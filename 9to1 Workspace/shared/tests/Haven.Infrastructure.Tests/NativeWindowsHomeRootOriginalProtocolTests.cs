using System.Buffers.Binary;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Owning framing/callback/custody controls. These issue no publisher,
/// installation, kernel peer, permission or Home-ready proof.</summary>
public sealed class NativeWindowsHomeRootOriginalProtocolTests
{
    [Fact]
    public async Task Actual_decoded_response_is_retained_before_caller_publication_refusal()
    {
        var request = Guid.NewGuid();
        var response = new NativeWindowsHomeRootWire.Response(1, request, true, null) { ReadId = Guid.NewGuid() };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var frame = new byte[bytes.Length + 4]; BinaryPrimitives.WriteInt32LittleEndian(frame, bytes.Length); bytes.CopyTo(frame, 4);
        var actual = new MemoryStream(frame, writable: false);
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(actual);
        NativeWindowsHomeRootWire.Response? captured = null;
        var foreign = new IOException("actual post-decode callback refusal");
        source.BindOriginalCallerCallback(body => { body(); if (captured is not null) throw foreign; });
        var original = NativeWindowsHomeRootWire.ReadAsync<NativeWindowsHomeRootWire.Response>(actual, source,
            CancellationToken.None, value => captured = value);
        var verified = false; Exception? bodyFailure = null;
        try
        {
            var failed = await Record.ExceptionAsync(() => original); Assert.NotNull(failed);
            Assert.True(original.IsFaulted); Assert.NotNull(captured);
            Assert.Equal(response, captured); Assert.Equal(response.ReadId, captured!.ReadId);
            Assert.Same(foreign, Assert.Single(source.OriginalErrors));
            Assert.All(source.OriginalTasks, raw => Assert.True(raw.IsCompletedSuccessfully));
            verified = true;
        }
        catch (Exception cause) { bodyFailure = cause; }
        await JoinFixtureAsync(bodyFailure, [(original, verified)], actual);
    }

    [Fact]
    public async Task Canceled_same_raw_write_and_independent_foreign_OCE_remain_faulted_with_both_occurrences()
    {
        var actual = new HeldWriteStream();
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(actual);
        var retained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var foreign = new OperationCanceledException("independent synchronous post-write refusal", new CancellationToken(true));
        source.BindOriginalCallerCallback(body =>
        {
            body();
            if (source.OriginalTasks.Any(raw => ReferenceEquals(raw, actual.OriginalWrite.Task)))
            { retained.TrySetResult(); throw foreign; }
        });
        var original = NativeWindowsHomeRootWire.WriteAsync(actual,
            new NativeWindowsHomeRootWire.Request(1, Guid.NewGuid(), "observe-current"), source, CancellationToken.None);
        var verified = false; Exception? bodyFailure = null;
        try
        {
            var winner = await Task.WhenAny(retained.Task, original).WaitAsync(TimeSpan.FromSeconds(15)); Assert.Same(retained.Task, winner);
            await retained.Task;
            Assert.Contains(source.OriginalTasks, raw => ReferenceEquals(raw, actual.OriginalWrite.Task));
            Assert.False(original.IsCompleted); Assert.Equal(1, actual.WriteCalls);
            actual.OriginalWrite.SetCanceled(new CancellationToken(true));
            var failed = await Record.ExceptionAsync(() => original); Assert.NotNull(failed);
            Assert.True(original.IsFaulted); Assert.False(original.IsCanceled);
            Assert.True(actual.OriginalWrite.Task.IsCanceled);
            Assert.Contains(source.OriginalErrors, cause => ReferenceEquals(cause, foreign));
            Assert.Contains(source.OriginalErrors, cause => cause is TaskCanceledException canceled &&
                ReferenceEquals(canceled.Task, actual.OriginalWrite.Task));
            Assert.All(source.OriginalErrors, cause => Assert.True(ReferenceEquals(cause, foreign) ||
                cause is TaskCanceledException canceled && ReferenceEquals(canceled.Task, actual.OriginalWrite.Task) ||
                cause is AggregateException ownSync && ownSync.InnerExceptions.Count == 1 && ReferenceEquals(ownSync.InnerExceptions[0], foreign)));
            verified = true;
        }
        catch (Exception cause) { bodyFailure = cause; }
        finally { actual.OriginalWrite.TrySetCanceled(new CancellationToken(true)); }
        await JoinFixtureAsync(bodyFailure, [(original, verified), (actual.OriginalWrite.Task, verified)], actual);
    }

    [NonWindowsFact]
    public async Task Actual_root_startup_retainer_rejects_restored_context_self_join_and_reports_missing_platform_without_launch()
    {
        var actual = new NativeWindowsHomeRootServiceRuntime(Path.Combine(Path.GetTempPath(), "root-protocol-uncreated-" + Guid.NewGuid().ToString("N") + ".json"));
        var restored = ExecutionContext.Capture() ?? throw new InvalidOperationException("The actual fixture context is required.");
        var retained = new List<Task>(); var calls = 0; Exception? bodyFailure = null; Task? close = null;
        var original = actual.StartOriginalWithinSourceAsync(body => body(), raw =>
        {
            retained.Add(raw); calls++;
            ExecutionContext.Run(restored, _ => Assert.Throws<InvalidOperationException>(() =>
            { _ = actual.CloseAndDrainOriginalAsync(); }), null);
        }, CancellationToken.None);
        try
        {
            var observed = await original;
            Assert.Equal(1, calls); Assert.Same(original, Assert.Single(retained));
            Assert.True(actual.IsIssuedOriginalStartup(observed)); Assert.False(observed.HasAcceptedOriginalHomeLaunch);
            Assert.Equal("InstalledWindowsRootRequired", observed.MissingPrerequisite);
            Assert.Throws<UnauthorizedAccessException>(() =>
            { _ = actual.StartOriginalControlWithinSourceAsync(observed, body => body(), retained.Add, CancellationToken.None); });
            actual.RequestOriginalRetirement(); close = actual.CloseAndDrainOriginalAsync(); await close;
            Assert.Same(close, actual.OriginalClose); Assert.Same(close, actual.CloseAndDrainOriginalAsync());
            Assert.True(close.IsCompletedSuccessfully);
        }
        catch (Exception cause) { bodyFailure = cause; }
        finally
        {
            try { close ??= actual.CloseAndDrainOriginalAsync(); }
            catch (Exception cause) { bodyFailure = bodyFailure is null ? cause : new AggregateException(bodyFailure, cause); }
        }
        var joins = new List<(Task, bool)> { (original, false) }; if (close is not null) joins.Add((close, false));
        await JoinFixtureAsync(bodyFailure, joins, null);
    }

    private static async Task JoinFixtureAsync(Exception? body, IEnumerable<(Task Actual, bool ExpectedVerified)> original,
        IDisposable? resource)
    {
        var errors = new List<Exception>(); if (body is not null) errors.Add(body);
        foreach (var (actual, expected) in original)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { if (!expected) errors.Add(actual.Exception ?? cause); }
        try { resource?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual protocol fixture/body/independent cleanup failed.", errors);
    }
    private sealed class NonWindowsFactAttribute : FactAttribute
    { public NonWindowsFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "This exact absence control requires a non-Windows platform; it supplies no installed Windows proof."; } }
    private sealed class HeldWriteStream : Stream
    {
        internal readonly TaskCompletionSource OriginalWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int WriteCalls;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { WriteCalls++; return new(OriginalWrite.Task); }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
