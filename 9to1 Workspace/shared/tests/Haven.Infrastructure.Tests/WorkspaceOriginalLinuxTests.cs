using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual supported Linux kernel/filesystem/process controls with a synthetic private
/// issuer. They prove this physical boundary, not a Home grant, OS jail, external-writer CAS,
/// installed Windows acceptance, or success of a child command's unobserved external effects.</summary>
public sealed class WorkspaceOriginalLinuxTests
{
    [LinuxOriginalFact]
    public async Task Read_preserves_full_original_text_without_mutation_receipt()
    {
        using var rig = new Rig("read_file", ("path", "code.cs"));
        var text = new string('a', 150_000) + "\nlast source"; File.WriteAllText(rig.Path("code.cs"), text);
        Assert.Equal(text, await rig.Invocation.Tools.ReadTextAsync(rig.Root, "code.cs", CancellationToken.None));
        var outcome = await rig.Outcome(true); Assert.Null(outcome.OriginalReceiptReference);
        Assert.Empty(outcome.OriginalErrors); Assert.Empty(outcome.Effects); Assert.True(outcome.KnownNoEffect);
    }

    [LinuxOriginalFact]
    public async Task Existing_file_commit_preserves_mode_and_has_one_authentic_physical_receipt()
    {
        using var rig = new Rig("write_file", ("path", "code.cs"), ("content", "new source"));
        File.WriteAllText(rig.Path("code.cs"), "old source");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead;
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(rig.Path("code.cs"), mode);
        await rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "code.cs", "new source", CancellationToken.None);
        var outcome = await rig.Outcome(true);
        Assert.Equal("new source", File.ReadAllText(rig.Path("code.cs")));
        if (OperatingSystem.IsLinux()) Assert.Equal(mode, File.GetUnixFileMode(rig.Path("code.cs")));
        Assert.NotNull(outcome.OriginalReceiptReference); Assert.Empty(outcome.OriginalErrors);
        Assert.True(rig.Source.ValidateOriginalOutcome(rig.Invocation, outcome));
        Assert.False(rig.Source.ValidateOriginalOutcome(rig.Invocation, outcome with { }));
        Assert.Equal(2, rig.Fence.NativeEffects); Assert.Equal(0, rig.Fence.LivePins);
        Assert.Empty(Directory.EnumerateFiles(rig.Root, ".haven-anonymous-*"));
        var effect = Assert.Single(outcome.Effects); Assert.True(effect.Admitted && effect.EffectKnown && effect.TerminalOutcomeKnown);
    }

    [LinuxOriginalFact]
    public async Task New_file_stays_private_and_missing_parent_refuses_before_staging()
    {
        using (var rig = new Rig("write_file", ("path", "new.cs"), ("content", "created")))
        {
            await rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "new.cs", "created", CancellationToken.None);
            Assert.NotNull((await rig.Outcome(true)).OriginalReceiptReference);
            if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(rig.Path("new.cs")));
        }
        using (var rig = new Rig("write_file", ("path", "missing/new.cs"), ("content", "created")))
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "missing/new.cs", "created", CancellationToken.None));
            var outcome = await rig.Outcome(false); Assert.True(outcome.KnownNoEffect); Assert.Null(outcome.OriginalReceiptReference);
            Assert.Equal(0, rig.Fence.NativeEffects); Assert.False(Directory.Exists(rig.Path("missing")));
        }
    }

    [LinuxOriginalFact]
    public async Task Changed_expected_preimage_is_refused_and_external_content_is_conserved()
    {
        var changes = JsonSerializer.Serialize(new[] { new { path = "code.cs", content = "edited", expectedSha256 = WorkspaceToolOriginalDigest.Text("old") } });
        using var rig = new Rig("apply_change_set", ("changes_json", changes));
        File.WriteAllText(rig.Path("code.cs"), "external winner");
        await Assert.ThrowsAsync<IOException>(() => rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "code.cs", "edited", CancellationToken.None));
        var outcome = await rig.Outcome(false); Assert.True(outcome.KnownNoEffect); Assert.Null(outcome.OriginalReceiptReference);
        Assert.Equal("external winner", File.ReadAllText(rig.Path("code.cs"))); Assert.Equal(0, rig.Fence.NativeEffects);
    }

    [LinuxOriginalFact]
    public async Task Final_policy_refusal_leaves_target_and_no_linked_stage_but_retains_unknown_admission()
    {
        using var rig = new Rig("write_file", ("path", "code.cs"), ("content", "edited"));
        File.WriteAllText(rig.Path("code.cs"), "old"); var exact = new UnauthorizedAccessException("actual final revocation");
        rig.Fence.RefuseCall = 2; rig.Fence.NativeRefusal = exact;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "code.cs", "edited", CancellationToken.None));
        Assert.True(Contains(error, exact)); var outcome = await rig.Outcome(false);
        Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown); Assert.False(outcome.KnownNoEffect);
        Assert.Equal("old", File.ReadAllText(rig.Path("code.cs"))); Assert.Empty(Directory.EnumerateFiles(rig.Root, ".haven-anonymous-*"));
        Assert.True(Contains(outcome.OriginalErrors, exact));
    }

    [LinuxOriginalFact]
    public async Task Root_replacement_after_acquisition_cannot_redirect_read_or_create()
    {
        using var rig = new Rig("write_file", ("path", "code.cs"), ("content", "edited"));
        var retained = rig.Root + "-original"; Directory.Move(rig.Root, retained); Directory.CreateDirectory(rig.Root);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "code.cs", "edited", CancellationToken.None));
            var outcome = await rig.Outcome(false); Assert.True(outcome.KnownNoEffect); Assert.Equal(0, rig.Fence.NativeEffects);
            Assert.False(File.Exists(rig.Path("code.cs"))); Assert.False(File.Exists(System.IO.Path.Combine(retained, "code.cs")));
        }
        finally { Directory.Delete(retained, true); }
    }

    [LinuxOriginalFact]
    public async Task Symlink_and_hardlink_aliases_refuse_before_text_egress_or_staging()
    {
        var outside = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astra-linux-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var secret = System.IO.Path.Combine(outside, "secret"); File.WriteAllText(secret, "outside secret");
            using (var rig = new Rig("read_file", ("path", "link")))
            {
                File.CreateSymbolicLink(rig.Path("link"), secret);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Invocation.Tools.ReadTextAsync(rig.Root, "link", CancellationToken.None));
                Assert.True((await rig.Outcome(false)).KnownNoEffect);
            }
            using (var rig = new Rig("read_file", ("path", "alias")))
            {
                Assert.Equal(0, Link(secret, rig.Path("alias")));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Invocation.Tools.ReadTextAsync(rig.Root, "alias", CancellationToken.None));
                Assert.True((await rig.Outcome(false)).KnownNoEffect);
            }
            Assert.Equal("outside secret", File.ReadAllText(secret));
        }
        finally { Directory.Delete(outside, true); }
    }

    [LinuxOriginalFact]
    public async Task Real_read_size_limit_refuses_without_truncating_source_to_a_success()
    {
        using var rig = new Rig("read_file", ("path", "large.cs")); File.WriteAllText(rig.Path("large.cs"), new string('x', 4 * 1024 * 1024 + 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Invocation.Tools.ReadTextAsync(rig.Root, "large.cs", CancellationToken.None));
        var outcome = await rig.Outcome(false); Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.KnownNoEffect);
    }

    [LinuxOriginalFact]
    public async Task Actual_nonzero_test_exit_is_known_once_executed_and_is_not_test_success()
    {
        const string command = "printf 'actualout'; printf 'actualerr' >&2; exit 7";
        using var rig = new Rig("run_tests", ("command", command), ("timeout_seconds", 10));
        var request = WorkspaceToolProcessRequestFactory.CreateOriginal(rig.Root, command, 10);
        var result = await rig.Invocation.Tools.RunProcessAsync(request, CancellationToken.None);
        Assert.Equal(7, result.ExitCode); Assert.False(result.TimedOut); Assert.Equal("actualout", result.StandardOutput); Assert.Equal("actualerr", result.StandardError);
        var outcome = await rig.Outcome(true); var effect = Assert.Single(outcome.Effects);
        Assert.NotNull(outcome.OriginalReceiptReference); Assert.Equal(7, effect.ExitCode);
        Assert.True(effect.EffectKnown && effect.TerminalOutcomeKnown); Assert.False(outcome.OutcomeUnknown); Assert.Empty(outcome.OriginalErrors);
        Assert.Equal(1, rig.Fence.NativeEffects); Assert.Equal(0, rig.Fence.LivePins);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Invocation.Tools.RunProcessAsync(request, CancellationToken.None));
        // Receipt certifies actual request completion, never exit-zero or business/test acceptance.
        Assert.False(result.ExitCode == 0 && !result.TimedOut);
    }

    [LinuxOriginalFact]
    public async Task Argument_vector_preserves_quotes_newline_and_actual_owned_working_directory()
    {
        const string command = "printf '%s\\n' \"$PWD\"; printf '%s' 'line1\nline2 \"quoted\"'; printf 'stderr' >&2";
        using var rig = new Rig("run_command", ("command", command), ("timeout_seconds", 10));
        var request = WorkspaceToolProcessRequestFactory.CreateOriginal(rig.Root, command, 10);
        Assert.Equal("", request.Arguments); Assert.Equal(new[] { "-c", command }, request.ArgumentList);
        var result = await rig.Invocation.Tools.RunProcessAsync(request, CancellationToken.None);
        Assert.Equal(0, result.ExitCode); Assert.StartsWith(rig.Root + "\n", result.StandardOutput);
        Assert.EndsWith("line1\nline2 \"quoted\"", result.StandardOutput); Assert.Equal("stderr", result.StandardError);
        Assert.NotNull((await rig.Outcome(true)).OriginalReceiptReference);
    }

    [LinuxOriginalFact]
    public async Task Conflicting_or_changed_argument_vector_refuses_before_original_start()
    {
        const string command = "printf original";
        using var rig = new Rig("run_command", ("command", command), ("timeout_seconds", 10));
        var expected = WorkspaceToolProcessRequestFactory.CreateOriginal(rig.Root, command, 10);
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Invocation.Tools.RunProcessAsync(expected with { Arguments = "-c other" }, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Invocation.Tools.RunProcessAsync(expected with { ArgumentList = new[] { "-c", "printf changed" } }, CancellationToken.None));
        var outcome = await rig.Outcome(false); Assert.True(outcome.KnownNoEffect); Assert.Empty(outcome.Effects); Assert.Equal(0, rig.Fence.NativeEffects);
    }

    [LinuxOriginalFact]
    public async Task Process_timeout_joins_actual_child_and_pipes_but_refuses_completed_request_receipt()
    {
        const string command = "echo $$ > child.pid; printf actualout; printf actualerr >&2; exec sleep 30";
        using var rig = new Rig("run_command", ("command", command), ("timeout_seconds", 1));
        var actual = rig.Invocation.Tools.RunProcessAsync(WorkspaceToolProcessRequestFactory.CreateOriginal(rig.Root, command, 1), CancellationToken.None);
        var result = await actual; Assert.True(result.TimedOut); Assert.Equal("actualout", result.StandardOutput); Assert.Equal("actualerr", result.StandardError);
        var pid = int.Parse(File.ReadAllText(rig.Path("child.pid")).Trim()); AssertExited(pid);
        var outcome = await rig.Outcome(true); Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown);
        Assert.True(Assert.Single(outcome.Effects).TerminalOutcomeKnown); Assert.Equal(0, rig.Fence.LivePins);
    }

    [LinuxOriginalFact]
    public async Task Caller_cancellation_joins_actual_process_before_owned_root_is_closed()
    {
        const string command = "echo $$ > child.pid; exec sleep 30";
        using var rig = new Rig("run_command", ("command", command), ("timeout_seconds", 30)); using var caller = new CancellationTokenSource();
        var actual = rig.Invocation.Tools.RunProcessAsync(WorkspaceToolProcessRequestFactory.CreateOriginal(rig.Root, command, 30), caller.Token);
        try
        {
            var timer = Stopwatch.StartNew(); var pid = 0;
            while (pid == 0)
            {
                if (actual.IsCompleted) await actual;
                if (File.Exists(rig.Path("child.pid"))) int.TryParse(File.ReadAllText(rig.Path("child.pid")).Trim(), out pid);
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10)); if (pid == 0) await Task.Delay(10);
            }
            caller.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual); AssertExited(pid);
            var outcome = await rig.Outcome(false); Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown);
        }
        finally { caller.Cancel(); try { await actual; } catch { } await rig.Outcome(false); }
    }

    [LinuxOriginalFact]
    public async Task Actual_committed_write_and_compound_pin_cleanup_remain_unknown_without_receipt()
    {
        using var rig = new Rig("write_file", ("path", "code.cs"), ("content", "edited"));
        File.WriteAllText(rig.Path("code.cs"), "old"); var first = new IOException("first real cleanup"); var second = new OperationCanceledException("faulted cleanup sibling");
        rig.Fence.PinErrors = [first, second];
        var error = await Assert.ThrowsAnyAsync<Exception>(() => rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "code.cs", "edited", CancellationToken.None));
        Assert.True(Contains(error, first)); Assert.True(Contains(error, second)); Assert.Equal("edited", File.ReadAllText(rig.Path("code.cs")));
        var outcome = await rig.Outcome(false); Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown);
        Assert.True(Contains(outcome.OriginalErrors, first)); Assert.True(Contains(outcome.OriginalErrors, second));
        Assert.True(Assert.Single(outcome.Effects).EffectKnown);
    }

    [LinuxOriginalFact]
    public async Task Close_waits_for_same_actual_write_pin_cleanup_without_early_outcome()
    {
        using var rig = new Rig("write_file", ("path", "code.cs"), ("content", "edited"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Fence.PinCleanup = () => { entered.TrySetResult(); return release.Task; };
        var actual = rig.Invocation.Tools.WriteTextAtomicAsync(rig.Root, "code.cs", "edited", CancellationToken.None); Task? close = null; Task<WorkspaceToolPhysicalOutcome>? outcome = null;
        try
        {
            await entered.Task; close = rig.Invocation.CloseAndDrainAsync(); outcome = rig.Invocation.CompleteOriginalAsync(true, CancellationToken.None);
            Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted); Assert.False(outcome.IsCompleted);
            Assert.Equal("edited", File.ReadAllText(rig.Path("code.cs")));
        }
        finally { release.TrySetResult(); try { await actual; } finally { if (close is not null) await close; if (outcome is not null) { await outcome; rig.CloseObserved = true; } } }
        Assert.NotNull((await rig.Outcome(true)).OriginalReceiptReference); Assert.Equal(0, rig.Fence.LivePins);
    }

    [LinuxOriginalFact]
    public async Task Root_rename_after_actual_start_does_not_adopt_replacement_and_is_uncertain()
    {
        const string command = "printf oldroot > proof; exit 0";
        using var rig = new Rig("run_command", ("command", command), ("timeout_seconds", 10)); var retained = rig.Root + "-retained";
        rig.Fence.AfterNative = () => { Directory.Move(rig.Root, retained); Directory.CreateDirectory(rig.Root); };
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => rig.Invocation.Tools.RunProcessAsync(WorkspaceToolProcessRequestFactory.CreateOriginal(rig.Root, command, 10), CancellationToken.None));
            Assert.True(error is UnauthorizedAccessException || error is AggregateException);
            var outcome = await rig.Outcome(false); Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown);
            Assert.Equal("oldroot", File.ReadAllText(System.IO.Path.Combine(retained, "proof"))); Assert.False(File.Exists(rig.Path("proof")));
            Assert.Equal(0, Assert.Single(outcome.Effects).ExitCode); Assert.True(Assert.Single(outcome.Effects).TerminalOutcomeKnown);
        }
        finally { Directory.Delete(retained, true); }
    }

    [LinuxOriginalFact]
    public async Task Fifo_without_writer_is_refused_before_read_egress_or_write_stage_and_real_close_settles()
    {
        using var rig = new Rig("read_file", ("path", "pipe"));
        Assert.Equal(0, Mkfifo(rig.Path("pipe"), 0x180));
        var actual = Task.Run(() => rig.Invocation.Tools.ReadTextAsync(rig.Root, "pipe", CancellationToken.None));
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actual.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.IsType<UnauthorizedAccessException>(error); Assert.False(actual.IsCanceled);
            var outcome = await rig.Outcome(false); Assert.True(outcome.KnownNoEffect); Assert.Equal(0, rig.Fence.NativeEffects);
        }
        finally
        {
            // If a regression performs a blocking native open, an actual fixture-owned peer
            // descriptor releases it before its SAME Task/drain/temporary-root cleanup is joined.
            if (!actual.IsCompleted)
            {
                var fd = Open(rig.Path("pipe"), 2 | 0x800 | 0x80000, 0);
                Assert.True(fd >= 0);
                using var releaseWriter = new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)fd, true);
                try { await actual; } catch { }
            }
            else try { await actual; } catch { }
            await rig.Outcome(false);
        }
        using var write = new Rig("apply_change_set", ("changes_json", JsonSerializer.Serialize(new[] { new { path = "pipe", content = "new", expectedSha256 = WorkspaceToolOriginalDigest.Text("") } })));
        Assert.Equal(0, Mkfifo(write.Path("pipe"), 0x180));
        var actualWrite = Task.Run(() => write.Invocation.Tools.WriteTextAtomicAsync(write.Root, "pipe", "new", CancellationToken.None));
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actualWrite.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.IsType<UnauthorizedAccessException>(error);
            var refused = await write.Outcome(false); Assert.True(refused.KnownNoEffect); Assert.Equal(0, write.Fence.NativeEffects);
        }
        finally
        {
            if (!actualWrite.IsCompleted)
            {
                var fd = Open(write.Path("pipe"), 2 | 0x800 | 0x80000, 0); Assert.True(fd >= 0);
                using var releaseWriter = new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)fd, true);
                try { await actualWrite; } catch { }
            }
            else try { await actualWrite; } catch { }
            await write.Outcome(false);
        }
    }

    [Theory]
    [InlineData(0x40U)]
    [InlineData(0x80U)]
    public void Missing_consumed_statx_timestamp_fields_refuse_version_observation(uint missing)
    {
        var raw = new byte[256]; var mask = 0x100U | 0x1U | 0x4U | 0x200U | 0x40U | 0x80U;
        BitConverter.GetBytes(mask & ~missing).CopyTo(raw, 0);
        var parser = typeof(WorkspaceToolService).GetMethod("ParseLinuxIdentity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => parser.Invoke(null, [raw]));
        Assert.IsType<PlatformNotSupportedException>(error.InnerException);
        BitConverter.GetBytes(mask).CopyTo(raw, 0); Assert.NotNull(parser.Invoke(null, [raw]));
    }

    public sealed class LinuxOriginalFactAttribute : FactAttribute
    { public LinuxOriginalFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires actual supported Linux descriptor/kernel/process execution; this does not certify Windows."; } }
    private static bool Contains(IEnumerable<Exception> errors, Exception exact) => errors.Any(value => Contains(value, exact));
    private static bool Contains(Exception error, Exception exact) => ReferenceEquals(error, exact) || error is AggregateException compound && Contains(compound.InnerExceptions, exact);
    private static void AssertExited(int pid) { try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); } catch (ArgumentException) { } }
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)] private static extern int Mkfifo(string path, uint mode);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "link", SetLastError = true)] private static extern int Link(string oldPath, string newPath);

    private sealed class Rig : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astra-original-linux-" + Guid.NewGuid().ToString("N"));
        public Fence Fence { get; }
        public WorkspaceToolService Source { get; }
        public IWorkspaceOriginalInvocation Invocation { get; }
        public bool CloseObserved;
        public Rig(string name, params (string Key, object Value)[] args)
        {
            Directory.CreateDirectory(Root); Fence = new(Root, new(name, args.ToDictionary(value => value.Key, value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal)));
            Source = new(new Issuer(Fence));
            try { Invocation = Source.AcquireOriginalInvocation(Fence); }
            catch { Directory.Delete(Root, true); throw; }
        }
        public string Path(string relative) => System.IO.Path.Combine(Root, relative);
        public async Task<WorkspaceToolPhysicalOutcome> Outcome(bool succeeded)
        { var actual = await Invocation.CompleteOriginalAsync(succeeded, CancellationToken.None); CloseObserved = true; return actual; }
        public void Dispose()
        {
            var errors = new List<Exception>();
            try { Invocation.CloseAndDrainAsync().GetAwaiter().GetResult(); }
            catch (Exception error) { if (!CloseObserved) errors.Add(error); }
            try { Directory.Delete(Root, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Linux fixture original teardown failed.", errors);
        }
    }
    private sealed class Issuer(Fence actual) : IWorkspaceToolFinalFenceAuthority
    { public bool IsIssuedOriginal(IWorkspaceToolFinalFence value) => ReferenceEquals(actual, value); }
    private sealed class Fence(string root, OllamaToolCall call) : IWorkspaceToolFinalFence
    {
        public TaskRunAttemptAdmission OriginalAttempt => throw new InvalidOperationException("Synthetic physical issuer; no actor, Task or Home authority is manufactured.");
        public Guid ActionId { get; } = Guid.NewGuid(); public string CanonicalWorkspaceRoot => root; public OllamaToolCall OriginalCall => call;
        public int NativeEffects; public int LivePins; public int RefuseCall; public Exception? NativeRefusal; public Action? AfterNative;
        public Exception[]? PinErrors; public Func<Task>? PinCleanup;
        public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); LivePins++; return ValueTask.FromResult<IAsyncDisposable?>(new Pin(this)); }
        public void DemandOriginalEffect(string actualRoot, WorkspaceToolEffectKind kind, string target, string digest) => Assert.Equal(root, actualRoot);
        public T RunOriginalEffect<T>(string actualRoot, WorkspaceToolEffectKind kind, string target, string digest, Func<T> body)
        {
            Assert.Equal(root, actualRoot); Assert.Equal(1, LivePins);
            if (NativeRefusal is not null && NativeEffects + 1 == RefuseCall) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(NativeRefusal).Throw();
            NativeEffects++; var value = body(); AfterNative?.Invoke(); return value;
        }
        private sealed class Pin(Fence owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.LivePins--;
                if (owner.PinCleanup is { } cleanup) return new(cleanup());
                if (owner.PinErrors is not { } errors) return ValueTask.CompletedTask;
                var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); actual.SetException(errors); return new(actual.Task);
            }
        }
    }
}
