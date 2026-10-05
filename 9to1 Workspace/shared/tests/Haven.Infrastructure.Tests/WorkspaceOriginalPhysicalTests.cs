using System.Diagnostics;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Physical boundary controls using synthetic issuer fixtures, real temporary files and
/// actual child processes. These prove the configured source checks; they do not grant Home
/// authority or certify installed/native presentation, public hosting, or external trust.</summary>
public sealed class WorkspaceOriginalPhysicalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-original-physical-" + Guid.NewGuid().ToString("N"));
    public WorkspaceOriginalPhysicalTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Missing_or_foreign_configured_issuer_cannot_acquire_original()
    {
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceToolService().AcquireOriginalInvocation(fence));
        Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceToolService(new Issuer(null)).AcquireOriginalInvocation(fence));
        Assert.False(File.Exists(Path.Combine(_root, "a.txt")));
    }

    [WindowsPhysicalFact]
    public async Task Missing_read_before_creation_does_not_hide_actual_committed_write()
    {
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        var source = new WorkspaceToolService(new Issuer(fence));
        await using var invocation = source.AcquireOriginalInvocation(fence);
        await Assert.ThrowsAsync<FileNotFoundException>(() => invocation.Tools.ReadTextAsync(_root, "a.txt", CancellationToken.None));
        await invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None);
        var outcome = await invocation.CompleteOriginalAsync(true, CancellationToken.None);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.NotNull(outcome.OriginalReceiptReference);
        Assert.Empty(outcome.OriginalErrors);
        var effect = Assert.Single(outcome.Effects);
        Assert.True(effect.Admitted && effect.EffectKnown && effect.TerminalOutcomeKnown);
        Assert.True(source.ValidateOriginalOutcome(invocation, outcome));
        Assert.False(source.ValidateOriginalOutcome(invocation, outcome with { }));
        Assert.False(new WorkspaceToolService(new Issuer(fence)).ValidateOriginalOutcome(invocation, outcome));
        Assert.Equal(2, fence.NativeEffects);
        Assert.Equal(0, fence.LivePins);
    }

    [Theory]
    [InlineData("other.txt", "new")]
    [InlineData("a.txt", "other")]
    public async Task Exact_original_target_and_content_are_required_before_native_effect(string target, string content)
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "old");
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invocation.Tools.WriteTextAtomicAsync(_root, target, content, CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.True(outcome.KnownNoEffect);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "other.txt")));
        Assert.Equal(0, fence.NativeEffects);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.haven.tmp.*", SearchOption.AllDirectories));
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [Fact]
    public async Task Captured_call_mutation_is_denied_even_from_same_issuer_object()
    {
        var arguments = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("a.txt"), ["content"] = JsonSerializer.SerializeToElement("new") };
        var fence = new Fence(_root, new OllamaToolCall("write_file", arguments));
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        arguments["content"] = JsonSerializer.SerializeToElement("changed");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.Equal(0, fence.NativeEffects);
        Assert.False(File.Exists(Path.Combine(_root, "a.txt")));
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [WindowsPhysicalFact]
    public async Task Repeated_same_write_cannot_reexecute_or_mint_receipt_after_partial_failure()
    {
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        await invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.True(outcome.OutcomeUnknown);
        Assert.Equal(2, fence.NativeEffects);
        Assert.Single(outcome.Effects);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "a.txt")));
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [WindowsPhysicalFact]
    public async Task Replace_uses_actual_retained_original_preimage_and_exact_result()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "alpha beta beta");
        var fence = FenceFor("replace_in_file", ("path", "a.txt"), ("old_text", "beta"), ("new_text", "gamma"));
        var source = new WorkspaceToolService(new Issuer(fence));
        await using var invocation = source.AcquireOriginalInvocation(fence);
        Assert.Equal("alpha beta beta", await invocation.Tools.ReadTextAsync(_root, "a.txt", CancellationToken.None));
        await invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "alpha gamma beta", CancellationToken.None);
        var outcome = await invocation.CompleteOriginalAsync(true, CancellationToken.None);
        Assert.NotNull(outcome.OriginalReceiptReference);
        Assert.Equal("alpha gamma beta", File.ReadAllText(Path.Combine(_root, "a.txt")));
    }

    [WindowsPhysicalFact]
    public async Task Changed_replace_preimage_is_retained_without_overwriting_external_change()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "alpha beta");
        var fence = FenceFor("replace_in_file", ("path", "a.txt"), ("old_text", "beta"), ("new_text", "gamma"));
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        await invocation.Tools.ReadTextAsync(_root, "a.txt", CancellationToken.None);
        File.WriteAllText(Path.Combine(_root, "a.txt"), "external winner");
        await Assert.ThrowsAsync<IOException>(() => invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "alpha gamma", CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.Equal("external winner", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.Equal(0, fence.NativeEffects);
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [WindowsPhysicalFact]
    public async Task Final_policy_refusal_keeps_file_and_does_not_issue_receipt()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "old");
        var original = new UnauthorizedAccessException("actual policy changed");
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        fence.NativeRefusal = original;
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        var caught = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None));
        Assert.Same(original, caught);
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.Contains(outcome.OriginalErrors, error => ReferenceEquals(error, original));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.Equal(0, fence.LivePins);
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [WindowsPhysicalFact]
    public async Task Both_actual_pin_cleanup_faults_survive_confirmed_move_without_receipt()
    {
        var first = new IOException("first original cleanup");
        var second = new IOException("second original cleanup");
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        fence.PinErrors = [first, second];
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        var error = await Assert.ThrowsAsync<AggregateException>(() => invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None));
        Assert.Contains(first, error.InnerExceptions);
        Assert.Contains(second, error.InnerExceptions);
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.True(ContainsReference(outcome.OriginalErrors, first));
        Assert.True(ContainsReference(outcome.OriginalErrors, second));
        Assert.True(Assert.Single(outcome.Effects).EffectKnown);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.True(outcome.OutcomeUnknown);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "a.txt")));
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [WindowsPhysicalFact]
    public async Task Close_and_outcome_wait_for_same_held_original_cleanup()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        fence.PinCleanup = () => { entered.TrySetResult(); return release.Task; };
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        var originalWrite = invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None);
        Task? originalClose = null;
        Task<WorkspaceToolPhysicalOutcome>? originalOutcome = null;
        var failures = new List<Exception>();
        try
        {
            await Task.WhenAny(entered.Task, originalWrite);
            if (originalWrite.IsCompleted) await originalWrite;
            Assert.True(entered.Task.IsCompletedSuccessfully);
            originalClose = invocation.CloseAndDrainAsync();
            originalOutcome = invocation.CompleteOriginalAsync(true, CancellationToken.None);
            Assert.False(originalWrite.IsCompleted);
            Assert.False(originalClose.IsCompleted);
            Assert.False(originalOutcome.IsCompleted);
            Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "a.txt")));
            release.TrySetResult();
            await originalWrite;
            await originalClose;
            var outcome = await originalOutcome;
            Assert.NotNull(outcome.OriginalReceiptReference);
            Assert.True(source.ValidateOriginalOutcome(invocation, outcome));
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            // Always release the SAME retained original cleanup, even if an assertion failed.
            release.TrySetResult();
            await ObserveOriginalAsync(originalWrite, failures);
            originalClose ??= invocation.CloseAndDrainAsync();
            await ObserveOriginalAsync(originalClose, failures);
            if (originalOutcome is not null) await ObserveOriginalAsync(originalOutcome, failures);
        }
        ThrowFixtureFailures(failures);
    }

    [WindowsPhysicalFact]
    public async Task Faulted_original_cleanup_OCE_retains_fault_status_and_reference()
    {
        var exact = new OperationCanceledException("original cleanup fault, not original cancellation");
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        fence.PinErrors = [exact];
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        var original = invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None);
        var compound = await Assert.ThrowsAsync<AggregateException>(() => original);
        Assert.True(original.IsFaulted);
        Assert.False(original.IsCanceled);
        Assert.True(ContainsReference(compound.InnerExceptions, exact));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.True(ContainsReference(outcome.OriginalErrors, exact));
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.True(outcome.OutcomeUnknown);
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [WindowsPhysicalFact]
    public async Task Rollback_delete_refuses_replaced_file_and_never_uses_legacy_delete()
    {
        var changes = JsonSerializer.Serialize(new[] { new { path = "a.txt", content = "new" } });
        var fence = FenceFor("apply_change_set", ("changes_json", changes));
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        await invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None);
        File.WriteAllText(Path.Combine(_root, "a.txt"), "external writer");
        var rollback = Assert.IsAssignableFrom<IWorkspaceOriginalRollbackService>(invocation.Tools);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rollback.DeleteOriginalCreatedFileAsync(_root, "a.txt", CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.Equal("external writer", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.True(outcome.OutcomeUnknown);
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [WindowsPhysicalFact]
    public async Task Held_staged_handle_excludes_external_open_delete_and_rename()
    {
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        fence.AfterNativeEffect = () =>
        {
            if (fence.NativeEffects != 1) return;
            var temporary = Assert.Single(Directory.EnumerateFiles(_root, "*.haven.tmp.*"));
            Assert.Throws<IOException>(() => { using var foreign = File.OpenRead(temporary); });
            Assert.Throws<IOException>(() => File.Delete(temporary));
            Assert.Throws<IOException>(() => File.Move(temporary, temporary + ".foreign"));
        };
        var source = new WorkspaceToolService(new Issuer(fence));
        await using var invocation = source.AcquireOriginalInvocation(fence);
        await invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None);
        var outcome = await invocation.CompleteOriginalAsync(true, CancellationToken.None);
        Assert.NotNull(outcome.OriginalReceiptReference);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.haven.tmp.*"));
    }

    [WindowsPhysicalFact]
    public async Task Revocation_after_stage_refuses_rename_and_disposes_same_staged_file()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "old");
        var refusal = new UnauthorizedAccessException("revoked after stage");
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        fence.RefuseNativeCall = 2;
        fence.NativeRefusal = refusal;
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        var actual = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None));
        Assert.Same(refusal, actual);
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.True(outcome.OutcomeUnknown);
        Assert.False(outcome.KnownNoEffect);
        Assert.True(Assert.Single(outcome.Effects).Admitted);
        Assert.False(outcome.Effects[0].EffectKnown);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.haven.tmp.*"));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.Equal(0, fence.LivePins);
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [Fact]
    public async Task Process_fields_cannot_be_substituted_for_actual_declared_command()
    {
        var fence = FenceFor("run_command", ("command", "Write-Output 'declared'"));
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        var request = OperatingSystem.IsWindows() ? new ProcessRequest("cmd.exe", "/c echo wrong", _root, TimeSpan.FromSeconds(2))
            : new ProcessRequest("/bin/sh", "-c \"printf wrong\"", _root, TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invocation.Tools.RunProcessAsync(request, CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.True(outcome.KnownNoEffect);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.Equal(0, fence.NativeEffects);
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [Fact]
    public async Task Legacy_process_observes_real_exit_and_both_original_streams()
    {
        var request = OperatingSystem.IsWindows() ? new ProcessRequest("cmd.exe", "/c \"echo actualout & echo actualerr 1>&2 & exit /b 7\"", _root, TimeSpan.FromSeconds(3))
            : new ProcessRequest("/bin/sh", "-c \"printf actualout; printf actualerr >&2; exit 7\"", _root, TimeSpan.FromSeconds(3));
        var result = await new WorkspaceToolService().RunProcessAsync(request, CancellationToken.None);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("actualout", result.StandardOutput);
        Assert.Contains("actualerr", result.StandardError);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Timeout_joins_actual_child_and_both_streams_before_returning()
    {
        var request = OperatingSystem.IsWindows() ? new ProcessRequest("powershell.exe", "-NoProfile -NonInteractive -Command \"$PID | Set-Content child.pid; Write-Output originalout; [Console]::Error.WriteLine('originalerr'); Start-Sleep 20\"", _root, TimeSpan.FromSeconds(2))
            : new ProcessRequest("/bin/sh", "-c \"echo $$ > child.pid; printf originalout; printf originalerr >&2; exec sleep 20\"", _root, TimeSpan.FromSeconds(1));
        var result = await new WorkspaceToolService().RunProcessAsync(request, CancellationToken.None);
        Assert.True(result.TimedOut);
        Assert.Contains("originalout", result.StandardOutput);
        Assert.Contains("originalerr", result.StandardError);
        var pid = int.Parse(File.ReadAllText(Path.Combine(_root, "child.pid")).Trim());
        AssertChildExited(pid);
    }

    [Fact]
    public async Task Caller_cancellation_joins_actual_child_exit_instead_of_canceled_wait_substitute()
    {
        using var caller = new CancellationTokenSource();
        var request = OperatingSystem.IsWindows() ? new ProcessRequest("powershell.exe", "-NoProfile -NonInteractive -Command \"$PID | Set-Content child.pid; Start-Sleep 20\"", _root, TimeSpan.FromSeconds(30))
            : new ProcessRequest("/bin/sh", "-c \"echo $$ > child.pid; exec sleep 20\"", _root, TimeSpan.FromSeconds(30));
        var original = new WorkspaceToolService().RunProcessAsync(request, caller.Token);
        var failures = new List<Exception>();
        var observedOriginalCancellation = false;
        try
        {
            var deadline = Stopwatch.StartNew();
            var pid = 0;
            while (pid == 0)
            {
                if (original.IsCompleted) await original;
                var marker = Path.Combine(_root, "child.pid");
                if (File.Exists(marker)) int.TryParse(File.ReadAllText(marker).Trim(), out pid);
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "The actual child did not publish its own PID.");
                if (pid == 0) await Task.Delay(10);
            }
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            observedOriginalCancellation = original.IsCanceled;
            Assert.True(original.IsCanceled);
            AssertChildExited(pid);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            try { caller.Cancel(); } catch (Exception error) { failures.Add(error); }
            if (!observedOriginalCancellation) await ObserveOriginalAsync(original, failures);
            // Already observed actual cancellation still has a terminal owned task; no waiter
            // substitution or dropped process is allowed on an assertion/marker failure.
            else Assert.True(original.IsCompleted);
        }
        ThrowFixtureFailures(failures);
    }

    [WindowsPhysicalFact]
    public async Task Readonly_completion_is_real_observation_without_mutation_receipt()
    {
        File.WriteAllText(Path.Combine(_root, "read.txt"), "same original read");
        var fence = FenceFor("read_file", ("path", "read.txt"));
        var source = new WorkspaceToolService(new Issuer(fence));
        await using var invocation = source.AcquireOriginalInvocation(fence);
        Assert.Equal("same original read", await invocation.Tools.ReadTextAsync(_root, "read.txt", CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(true, CancellationToken.None);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.Empty(outcome.Effects);
        Assert.Empty(outcome.OriginalErrors);
        Assert.True(outcome.KnownNoEffect);
        Assert.True(source.ValidateOriginalOutcome(invocation, outcome));
    }

    [Fact]
    public async Task Strict_mutation_platform_capability_is_checked_before_any_stage()
    {
        if (OperatingSystem.IsWindows()) return; // Owning Windows cases exercise the real port.
        var fence = FenceFor("write_file", ("path", "nested/a.txt"), ("content", "new"));
        var source = new WorkspaceToolService(new Issuer(fence));
        var invocation = source.AcquireOriginalInvocation(fence);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => invocation.Tools.WriteTextAtomicAsync(_root, "nested/a.txt", "new", CancellationToken.None));
        var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
        Assert.True(outcome.KnownNoEffect);
        Assert.Null(outcome.OriginalReceiptReference);
        Assert.Equal(0, fence.NativeEffects);
        Assert.False(Directory.Exists(Path.Combine(_root, "nested")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
        await Assert.ThrowsAnyAsync<Exception>(() => invocation.CloseAndDrainAsync());

    }

    [Fact]
    public async Task Existing_symbolic_parent_is_refused_before_strict_read_egress()
    {
        var outside = Path.Combine(Path.GetTempPath(), "astra-outside-original-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside secret");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "redirect"), outside);
            var fence = FenceFor("read_file", ("path", "redirect/secret.txt"));
            var source = new WorkspaceToolService(new Issuer(fence));
            await using var invocation = source.AcquireOriginalInvocation(fence);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invocation.Tools.ReadTextAsync(_root, "redirect/secret.txt", CancellationToken.None));
            var outcome = await invocation.CompleteOriginalAsync(false, CancellationToken.None);
            Assert.True(outcome.KnownNoEffect);
            Assert.Null(outcome.OriginalReceiptReference);
            Assert.Equal("outside secret", File.ReadAllText(Path.Combine(outside, "secret.txt")));
        }
        finally { Directory.Delete(outside, recursive: true); }
    }

    [Fact]
    public void Strict_traversal_refuses_before_original_body_when_safe_child_owner_is_absent()
    {
        var fence = FenceFor("list_files", ("path", "."));
        var source = new WorkspaceToolService(new Issuer(fence));
        Assert.Throws<PlatformNotSupportedException>(() => source.AcquireOriginalInvocation(fence));
        Assert.True(Directory.Exists(source.ResolveWorkspacePath(_root, ".")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [WindowsPhysicalFact]
    public async Task Retained_root_parent_cannot_be_replaced_during_original_invocation()
    {
        var fence = FenceFor("write_file", ("path", "a.txt"), ("content", "new"));
        var source = new WorkspaceToolService(new Issuer(fence));
        await using var invocation = source.AcquireOriginalInvocation(fence);
        Assert.Throws<IOException>(() => Directory.Move(_root, _root + ".foreign"));
        await invocation.Tools.WriteTextAtomicAsync(_root, "a.txt", "new", CancellationToken.None);
        var outcome = await invocation.CompleteOriginalAsync(true, CancellationToken.None);
        Assert.NotNull(outcome.OriginalReceiptReference);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_root, "a.txt")));
    }

    public sealed class WindowsPhysicalFactAttribute : FactAttribute
    {
        public WindowsPhysicalFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires actual Windows file-handle rename/disposition; unsupported platform negatives run separately. This is not Windows acceptance.";
        }
    }

    private static async Task ObserveOriginalAsync(Task original, List<Exception> failures)
    {
        try { await original; }
        catch (Exception error)
        {
            if (original.Exception is { InnerExceptions.Count: > 0 } compound) failures.AddRange(compound.InnerExceptions);
            else failures.Add(error);
        }
    }
    private static void ThrowFixtureFailures(List<Exception> failures)
    {
        var exact = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (exact.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exact[0]).Throw();
        if (exact.Length != 0) throw new AggregateException("Original fixture assertions and owned cleanup failed.", exact);
    }

    private Fence FenceFor(string name, params (string Key, string Value)[] arguments) => new(_root,
        new OllamaToolCall(name, arguments.ToDictionary(value => value.Key, value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal)));
    private static bool ContainsReference(IEnumerable<Exception> errors, Exception exact) => errors.Any(error => ReferenceEquals(error, exact) || error is AggregateException compound && ContainsReference(compound.InnerExceptions, exact));
    private static void AssertChildExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }
    private sealed class Issuer(Fence? actual) : IWorkspaceToolFinalFenceAuthority
    {
        public bool IsIssuedOriginal(IWorkspaceToolFinalFence originalFence) => ReferenceEquals(actual, originalFence);
    }
    private sealed class Fence(string root, OllamaToolCall call) : IWorkspaceToolFinalFence
    {
        public TaskRunAttemptAdmission OriginalAttempt => throw new InvalidOperationException("Physical issuer fixture; no Home/task grant is manufactured.");
        public Guid ActionId { get; } = Guid.NewGuid();
        public string CanonicalWorkspaceRoot => root;
        public OllamaToolCall OriginalCall => call;
        public int NativeEffects { get; private set; }
        public int LivePins { get; private set; }
        public Exception? NativeRefusal { get; set; }
        public int RefuseNativeCall { get; set; } = 1;
        public Action? AfterNativeEffect { get; set; }
        public Exception[]? PinErrors { get; set; }
        public Func<Task>? PinCleanup { get; set; }
        public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LivePins++;
            return ValueTask.FromResult<IAsyncDisposable?>(new Pin(this));
        }
        public void DemandOriginalEffect(string canonicalWorkspaceRoot, WorkspaceToolEffectKind kind, string canonicalTarget, string exactContentOrRequestSha256) => Assert.Equal(root, canonicalWorkspaceRoot);
        public T RunOriginalEffect<T>(string canonicalWorkspaceRoot, WorkspaceToolEffectKind kind, string canonicalTarget, string exactContentOrRequestSha256, Func<T> originalNativeEffect)
        {
            if (NativeRefusal is not null && NativeEffects + 1 == RefuseNativeCall)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(NativeRefusal).Throw();
            Assert.Equal(1, LivePins);
            NativeEffects++;
            var result = originalNativeEffect();
            AfterNativeEffect?.Invoke(); // Synthetic fixture observer; no production callback.
            return result;
        }
        private sealed class Pin(Fence owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.LivePins--;
                if (owner.PinCleanup is { } cleanup) return new ValueTask(cleanup());
                if (owner.PinErrors is not { } errors) return ValueTask.CompletedTask;
                var original = new TaskCompletionSource();
                original.SetException(errors);
                return new ValueTask(original.Task);
            }
        }
    }
    public void Dispose() { Directory.Delete(_root, recursive: true); }
}
