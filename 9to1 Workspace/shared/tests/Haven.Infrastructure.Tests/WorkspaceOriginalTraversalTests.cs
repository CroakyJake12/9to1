using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Real retained-handle filesystem controls. The private read-fence fixture issues no
/// Task, actor, permission or Home grant; canonical admission is covered separately in Core.</summary>
public sealed class WorkspaceOriginalTraversalTests
{
    [TraversalFact]
    public async Task Nested_listing_coalesces_the_actual_task_preserves_bytes_and_has_no_effect_receipt()
    {
        await using var rig = new Rig("list_files", new { path = ".", max_depth = 3 });
        Directory.CreateDirectory(rig.Path("src/nested"));
        File.WriteAllText(rig.Path("README.md"), "original root bytes");
        File.WriteAllText(rig.Path("src/nested/code.cs"), "original nested bytes");
        var before = File.ReadAllBytes(rig.Path("src/nested/code.cs"));
        var actual = rig.Tools.ListOriginalFilesAsync(rig.Root, ".", 3, default);
        Assert.Same(actual, rig.Tools.ListOriginalFilesAsync(rig.Root, ".", 3, default));
        var observed = await actual;
        Assert.Contains(observed.Entries, value => Relative(value.RelativePath) == "README.md" && !value.IsDirectory);
        Assert.Contains(observed.Entries, value => Relative(value.RelativePath) == "src/nested" && value.IsDirectory);
        Assert.Contains(observed.Entries, value => Relative(value.RelativePath) == "src/nested/code.cs" && !value.IsDirectory && value.Size == before.Length);
        Assert.False(observed.EntryLimitReached || observed.DepthLimitReached || observed.ValidationLimitReached);
        Assert.Equal(before, File.ReadAllBytes(rig.Path("src/nested/code.cs")));
        Assert.Equal("original root bytes", File.ReadAllText(rig.Path("README.md")));
        await AssertReadOnlyOutcomeAsync(rig, true);
        Assert.Equal(0, rig.Fence.Effects); Assert.Equal(0, rig.Fence.LivePins);
        Assert.True(rig.Fence.NativeReads > 0); Assert.True(rig.Fence.Validations > 0);
    }

    [TraversalFact]
    public async Task Search_observes_line_numbers_and_reports_ignored_large_binary_and_invalid_utf8_exclusions()
    {
        await using var rig = new Rig("search_files", new { path = ".", query = "needle", max_results = 20 });
        Directory.CreateDirectory(rig.Path("src")); Directory.CreateDirectory(rig.Path(".git"));
        File.WriteAllText(rig.Path("src/code.cs"), "first\nneedle actual\nthird\nNEEDLE actual too\n", new UTF8Encoding(false));
        File.WriteAllText(rig.Path(".git/secret"), "needle ignored");
        File.WriteAllText(rig.Path("large.txt"), new string('x', 2 * 1024 * 1024 + 1) + "needle");
        File.WriteAllBytes(rig.Path("binary.bin"), [110, 101, 101, 100, 108, 101, 0, 1]);
        File.WriteAllBytes(rig.Path("invalid.txt"), [110, 101, 101, 100, 108, 101, 0xff, 0xfe]);
        var before = File.ReadAllBytes(rig.Path("src/code.cs"));
        var actual = rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "needle", 20, default);
        Assert.Same(actual, rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "needle", 20, default));
        var observed = await actual;
        Assert.Contains(observed.Matches, value => Relative(value.RelativePath) == "src/code.cs" && value.Line == 2 && value.Text.Contains("needle actual", StringComparison.Ordinal));
        Assert.DoesNotContain(observed.Matches, value => Relative(value.RelativePath).StartsWith(".git/", StringComparison.Ordinal));
        Assert.Equal(1, observed.IgnoredDirectoryCount); Assert.Equal(1, observed.LargeFileCount);
        Assert.Equal(2, observed.BinaryFileCount);
        Assert.False(observed.ResultLimitReached || observed.ByteLimitReached || observed.ValidationLimitReached);
        Assert.Equal(before, File.ReadAllBytes(rig.Path("src/code.cs")));
        await AssertReadOnlyOutcomeAsync(rig, true);
    }

    [TraversalFact]
    public async Task Match_limit_is_a_bounded_observation_and_never_unbounded_output()
    {
        await using var rig = new Rig("search_files", new { path = ".", query = "needle", max_results = 200 });
        File.WriteAllText(rig.Path("many.txt"), string.Join('\n', Enumerable.Repeat("needle actual", 240)));
        var observed = await rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "needle", 200, default);
        Assert.Equal(200, observed.Matches.Count); Assert.True(observed.ResultLimitReached);
        Assert.All(observed.Matches, value => Assert.Equal("many.txt", Relative(value.RelativePath)));
        await AssertReadOnlyOutcomeAsync(rig, true);
    }

    [TraversalFact]
    public async Task Deep_listing_stops_at_the_declared_depth_and_reports_unvisited_descendants()
    {
        await using var rig = new Rig("list_files", new { path = ".", max_depth = 1 });
        Directory.CreateDirectory(rig.Path("folder/deeper")); File.WriteAllText(rig.Path("folder/deeper/hidden.txt"), "actual source");
        var observed = await rig.Tools.ListOriginalFilesAsync(rig.Root, ".", 1, default);
        Assert.Contains(observed.Entries, value => Relative(value.RelativePath) == "folder");
        Assert.DoesNotContain(observed.Entries, value => Relative(value.RelativePath) == "folder/deeper/hidden.txt");
        Assert.True(observed.DepthLimitReached); await AssertReadOnlyOutcomeAsync(rig, true);
    }

    [TraversalFact]
    public async Task Large_directory_reports_the_actual_entry_or_validation_bound_instead_of_complete_coverage()
    {
        await using var rig = new Rig("list_files", new { path = ".", max_depth = 1 });
        for (var index = 0; index < 2050; index++) File.WriteAllText(rig.Path($"entry-{index:D4}.txt"), "source");
        var observed = await rig.Tools.ListOriginalFilesAsync(rig.Root, ".", 1, default);
        Assert.InRange(observed.Entries.Count, 1, 2000);
        Assert.True(observed.EntryLimitReached || observed.ValidationLimitReached);
        Assert.True(rig.Fence.Validations <= 48, "The physical owner must respect the declared fresh-validation budget.");
        Assert.Equal(2050, Directory.GetFiles(rig.Root).Length); await AssertReadOnlyOutcomeAsync(rig, true);
    }

    [TraversalFact]
    public async Task Search_reports_its_total_byte_budget_without_silently_claiming_every_file_was_scanned()
    {
        await using var rig = new Rig("search_files", new { path = ".", query = "absent", max_results = 20 });
        for (var index = 0; index < 10; index++) File.WriteAllText(rig.Path($"text-{index:D2}.txt"), new string('x', 2 * 1024 * 1024));
        var observed = await rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "absent", 20, default);
        Assert.Empty(observed.Matches);
        Assert.True(observed.ByteLimitReached || observed.ValidationLimitReached);
        Assert.Equal(0, observed.LargeFileCount); await AssertReadOnlyOutcomeAsync(rig, true);
    }

    [TraversalTheory]
    [InlineData("path")]
    [InlineData("depth")]
    [InlineData("query")]
    [InlineData("max_results")]
    public async Task Changed_captured_folder_depth_query_or_limit_refuses_before_native_read(string changed)
    {
        var list = changed is "path" or "depth";
        await using var rig = new Rig(list ? "list_files" : "search_files",
            list ? (object)new { path = ".", max_depth = 2 } : new { path = ".", query = "actual", max_results = 4 });
        Directory.CreateDirectory(rig.Path("other")); File.WriteAllText(rig.Path("actual.txt"), "actual source");
        var error = await Record.ExceptionAsync(async () =>
        {
            if (list) await rig.Tools.ListOriginalFilesAsync(rig.Root, changed == "path" ? "other" : ".", changed == "depth" ? 3 : 2, default);
            else await rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", changed == "query" ? "changed" : "actual", changed == "max_results" ? 5 : 4, default);
        });
        Assert.NotNull(error); rig.Expect(error!);
        Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
        Assert.Equal(0, rig.Fence.NativeReads); await AssertReadOnlyOutcomeAsync(rig, false);
    }

    [TraversalFact]
    public async Task Foreign_private_issuer_and_an_issued_effect_only_fence_cannot_enable_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-traversal-foreign-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var call = Call("list_files", new { path = ".", max_depth = 1 }); var actual = new ReadFence(root, call); var foreign = new ReadFence(root, call);
            var source = new WorkspaceToolService(new Issuer(actual));
            Assert.Throws<UnauthorizedAccessException>(() => source.AcquireOriginalInvocation(foreign));
            var effectOnly = new EffectOnlyFence(actual);
            var denied = Record.Exception(() => new WorkspaceToolService(new Issuer(effectOnly)).AcquireOriginalInvocation(effectOnly));
            Assert.NotNull(denied); Assert.True(denied is PlatformNotSupportedException or UnauthorizedAccessException);
            Assert.Equal(0, actual.NativeReads); Assert.Equal(0, foreign.NativeReads);
        }
        finally { Directory.Delete(root, true); }
    }

    [TraversalFact]
    public async Task Hardlinked_child_refuses_before_aliased_text_can_be_returned()
    {
        var outside = Path.Combine(Path.GetTempPath(), "haven-traversal-outside-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outside);
        try
        {
            var secret = Path.Combine(outside, "secret.txt"); File.WriteAllText(secret, "outside needle secret");
            await using var rig = new Rig("search_files", new { path = ".", query = "needle", max_results = 10 });
            if (OperatingSystem.IsWindows()) Assert.True(CreateHardLink(rig.Path("alias.txt"), secret, IntPtr.Zero));
            else Assert.Equal(0, Link(secret, rig.Path("alias.txt")));
            var actual = rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "needle", 10, default);
            var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
            Assert.Equal("outside needle secret", File.ReadAllText(secret)); await AssertReadOnlyOutcomeAsync(rig, false);
        }
        finally { Directory.Delete(outside, true); }
    }

    [LinuxTraversalFact]
    public async Task Symlink_and_fifo_children_fail_closed_without_a_text_egress_or_blocking_fifo_open()
    {
        var outside = Path.Combine(Path.GetTempPath(), "haven-traversal-symlink-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outside);
        try
        {
            var secret = Path.Combine(outside, "secret.txt"); File.WriteAllText(secret, "outside needle secret");
            await using (var rig = new Rig("search_files", new { path = ".", query = "needle", max_results = 10 }))
            {
                File.CreateSymbolicLink(rig.Path("alias.txt"), secret);
                var actual = rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "needle", 10, default);
                var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
                Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException); await AssertReadOnlyOutcomeAsync(rig, false);
            }
            await using (var rig = new Rig("search_files", new { path = ".", query = "needle", max_results = 10 }))
            {
                Assert.Equal(0, Mkfifo(rig.Path("pipe"), 0x180));
                // Run on a worker so an accidental blocking native FIFO open remains observable.
                var actual = Task.Run(() => rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "needle", 10, default));
                try
                {
                    var error = await Record.ExceptionAsync(() => actual.WaitAsync(TimeSpan.FromSeconds(3))); Assert.NotNull(error);
                    Assert.DoesNotContain(Leaves(error!), value => value is TimeoutException); rig.Expect(error!);
                    Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException); await AssertReadOnlyOutcomeAsync(rig, false);
                }
                finally
                {
                    if (!actual.IsCompleted)
                    {
                        var descriptor = Open(rig.Path("pipe"), 2 | 0x800 | 0x80000, 0); Assert.True(descriptor >= 0);
                        using var release = new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)descriptor, true);
                        var error = await Record.ExceptionAsync(() => actual); if (error is not null) rig.Expect(error);
                    }
                    else { var error = await Record.ExceptionAsync(() => actual); if (error is not null) rig.Expect(error); }
                }
            }
            Assert.Equal("outside needle secret", File.ReadAllText(secret));
        }
        finally { Directory.Delete(outside, true); }
    }

    [LinuxTraversalFact]
    public async Task Root_replacement_after_original_acquisition_cannot_redirect_directory_enumeration()
    {
        await using var rig = new Rig("list_files", new { path = ".", max_depth = 1 });
        File.WriteAllText(rig.Path("retained.txt"), "original source"); var retained = rig.Root + "-retained";
        Directory.Move(rig.Root, retained); Directory.CreateDirectory(rig.Root); File.WriteAllText(rig.Path("replacement.txt"), "replacement source");
        try
        {
            var actual = rig.Tools.ListOriginalFilesAsync(rig.Root, ".", 1, default);
            var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), value => value is UnauthorizedAccessException);
            Assert.Equal("original source", File.ReadAllText(Path.Combine(retained, "retained.txt")));
            Assert.Equal("replacement source", File.ReadAllText(rig.Path("replacement.txt"))); await AssertReadOnlyOutcomeAsync(rig, false);
        }
        finally { Directory.Delete(retained, true); }
    }

    [TraversalFact]
    public async Task Original_validation_task_ignoring_cancellation_is_retained_and_joined_by_close()
    {
        await using var rig = new Rig("list_files", new { path = ".", max_depth = 1 }); File.WriteAllText(rig.Path("source.txt"), "actual source");
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Fence.Validation = _ => { entered.TrySetResult(); return held.Task; };
        var actual = rig.Tools.ListOriginalFilesAsync(rig.Root, ".", 1, default); Task? close = null;
        try
        {
            await entered.Task; close = rig.Invocation.CloseAndDrainAsync();
            Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted); Assert.Equal(0, rig.Fence.NativeReads);
        }
        finally
        {
            held.TrySetResult(); var error = await Record.ExceptionAsync(() => actual); if (error is not null) rig.Expect(error);
            if (close is not null) { var cleanup = await Record.ExceptionAsync(() => close); if (cleanup is not null) rig.Expect(cleanup); }
        }
        Assert.True(held.Task.IsCompletedSuccessfully); await AssertReadOnlyOutcomeAsync(rig, false);
    }

    [TraversalTheory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task Faulted_validation_oce_siblings_remain_faults_and_actual_cancellation_remains_cancellation(int validationIndex, bool canceled)
    {
        await using var rig = new Rig("search_files", new { path = ".", query = "needle", max_results = 10 });
        File.WriteAllText(rig.Path("source.txt"), "needle actual source");
        var first = new OperationCanceledException("FAULTED original actor read"); var sibling = new IOException("Original actor read sibling");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (canceled) raw.SetCanceled(new CancellationToken(true)); else raw.SetException([first, sibling]);
        rig.Fence.Validation = _ => rig.Fence.Validations == validationIndex ? raw.Task : Task.CompletedTask;
        var actual = rig.Tools.SearchOriginalFilesAsync(rig.Root, ".", "needle", 10, default);
        var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
        Assert.Equal(canceled, raw.Task.IsCanceled); Assert.Equal(!canceled, raw.Task.IsFaulted);
        Assert.Equal(canceled, actual.IsCanceled); Assert.Equal(!canceled, actual.IsFaulted);
        if (!canceled)
        {
            Assert.Contains(Leaves(error!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(error!), value => ReferenceEquals(value, sibling));
        }
        if (validationIndex == 1) Assert.Equal(0, rig.Fence.NativeReads);
        else Assert.True(rig.Fence.NativeReads > 0, "The post-read control must settle after actual held-handle bytes were privately read.");
        var outcome = await AssertReadOnlyOutcomeAsync(rig, false);
        if (!canceled)
        {
            Assert.Contains(outcome.OriginalErrors.SelectMany(Leaves), value => ReferenceEquals(value, first));
            Assert.Contains(outcome.OriginalErrors.SelectMany(Leaves), value => ReferenceEquals(value, sibling));
        }
    }

    [TraversalFact]
    public async Task Actual_pin_cleanup_task_is_joined_and_compound_cleanup_fault_prevents_complete_observation()
    {
        await using var rig = new Rig("list_files", new { path = ".", max_depth = 1 }); File.WriteAllText(rig.Path("source.txt"), "source");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new IOException("Original pin cleanup"); var second = new OperationCanceledException("FAULTED cleanup sibling");
        rig.Fence.PinCleanup = raw.Task;
        var actual = rig.Tools.ListOriginalFilesAsync(rig.Root, ".", 1, default);
        try { await rig.Fence.PinCleanupEntered.Task; Assert.False(actual.IsCompleted); Assert.True(rig.Fence.NativeReads > 0); }
        finally { raw.TrySetException([first, second]); }
        var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error); rig.Expect(error!);
        Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        Assert.Contains(Leaves(error!), value => ReferenceEquals(value, first)); Assert.Contains(Leaves(error!), value => ReferenceEquals(value, second));
        var outcome = await AssertReadOnlyOutcomeAsync(rig, false);
        Assert.Contains(outcome.OriginalErrors.SelectMany(Leaves), value => ReferenceEquals(value, first));
        Assert.Contains(outcome.OriginalErrors.SelectMany(Leaves), value => ReferenceEquals(value, second));
        Assert.Equal(0, rig.Fence.LivePins);
    }

    private static string Relative(string path) => path.Replace('\\', '/');
    private static OllamaToolCall Call(string name, object args) => new(name,
        JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(value => value.Name, value => value.Value.Clone(), StringComparer.Ordinal));
    private static IEnumerable<Exception> Leaves(Exception error)
    {
        if (error is AggregateException group) foreach (var child in group.InnerExceptions) foreach (var leaf in Leaves(child)) yield return leaf;
        else yield return error;
    }
    private static async Task<WorkspaceToolPhysicalOutcome> AssertReadOnlyOutcomeAsync(Rig rig, bool succeeded)
    {
        var actual = await rig.Invocation.CompleteOriginalAsync(succeeded, default);
        Assert.Null(actual.OriginalReceiptReference); Assert.Empty(actual.Effects); Assert.True(actual.KnownNoEffect); Assert.False(actual.OutcomeUnknown);
        if (succeeded) Assert.Empty(actual.OriginalErrors);
        Assert.True(rig.Source.ValidateOriginalOutcome(rig.Invocation, actual));
        Assert.False(rig.Source.ValidateOriginalOutcome(rig.Invocation, actual with { }));
        foreach (var error in actual.OriginalErrors) rig.Expect(error);
        return actual;
    }

    public sealed class TraversalFactAttribute : FactAttribute
    {
        public TraversalFactAttribute() { if (!SupportedHost) Skip = "Requires real Windows or Linux x64/arm64 retained-handle traversal; ABI failure is a failure, not a skip."; }
    }
    public sealed class TraversalTheoryAttribute : TheoryAttribute
    {
        public TraversalTheoryAttribute() { if (!SupportedHost) Skip = "Requires real Windows or Linux x64/arm64 retained-handle traversal; ABI failure is a failure, not a skip."; }
    }
    public sealed class LinuxTraversalFactAttribute : FactAttribute
    {
        public LinuxTraversalFactAttribute() { if (!OperatingSystem.IsLinux() || !SupportedHost) Skip = "Requires actual supported Linux descriptor traversal."; }
    }
    private static bool SupportedHost => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
    [DllImport("libc", EntryPoint = "link", SetLastError = true)] private static extern int Link(string oldPath, string newPath);
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)] private static extern int Mkfifo(string path, uint mode);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags, uint mode);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string newName, string existingName, IntPtr security);

    private sealed class Rig : IAsyncDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "haven-original-traversal-" + Guid.NewGuid().ToString("N"));
        public ReadFence Fence { get; }
        public WorkspaceToolService Source { get; }
        public IWorkspaceOriginalInvocation Invocation { get; }
        public IWorkspaceOriginalTraversalService Tools { get; }
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        public Rig(string name, object args)
        {
            Directory.CreateDirectory(Root); Fence = new(Root, Call(name, args)); Source = new(new Issuer(Fence));
            try { Invocation = Source.AcquireOriginalInvocation(Fence); Tools = Assert.IsAssignableFrom<IWorkspaceOriginalTraversalService>(Invocation.Tools); }
            catch { Directory.Delete(Root, true); throw; }
        }
        public string Path(string relative) => System.IO.Path.Combine(Root, relative);
        public void Expect(Exception error) { foreach (var leaf in Leaves(error)) _expected.Add(leaf); }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>(); Task? actual = null;
            try { actual = Invocation.CloseAndDrainAsync(); await actual; }
            catch (Exception error)
            {
                var whole = (Exception?)actual?.Exception ?? error;
                if (Leaves(whole).Any(leaf => !_expected.Contains(leaf))) errors.Add(whole);
            }
            try { Directory.Delete(Root, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Original traversal fixture close and physical cleanup failed.", errors);
        }
    }
    private sealed class Issuer(IWorkspaceToolFinalFence actual) : IWorkspaceToolFinalFenceAuthority
    { public bool IsIssuedOriginal(IWorkspaceToolFinalFence value) => ReferenceEquals(actual, value); }
    private sealed class ReadFence(string root, OllamaToolCall call) : IWorkspaceOriginalReadFence
    {
        public TaskRunAttemptAdmission OriginalAttempt => throw new InvalidOperationException("Synthetic physical issuer; no canonical Task, actor or Home grant.");
        public Guid ActionId { get; } = Guid.NewGuid(); public string CanonicalWorkspaceRoot => root; public OllamaToolCall OriginalCall => call;
        public int Validations; public int NativeReads; public int Effects; public int LivePins;
        public Func<CancellationToken, Task>? Validation; public Task? PinCleanup;
        public TaskCompletionSource PinCleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask RevalidateOriginalReadAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Validations++; return Validation is { } actual ? new(actual(token)) : ValueTask.CompletedTask; }
        public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); LivePins++; return ValueTask.FromResult<IAsyncDisposable?>(new Pin(this)); }
        public T RunOriginalRead<T>(string actualRoot, string target, Func<T> finiteNativeRead, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Assert.Equal(root, actualRoot); Assert.Equal(1, LivePins);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Assert.True(string.Equals(root, target, comparison) || target.StartsWith(root + System.IO.Path.DirectorySeparatorChar, comparison));
            NativeReads++; return finiteNativeRead();
        }
        public void DemandOriginalEffect(string actualRoot, WorkspaceToolEffectKind kind, string target, string digest) => throw new InvalidOperationException("Read-only original attempted an effect.");
        public T RunOriginalEffect<T>(string actualRoot, WorkspaceToolEffectKind kind, string target, string digest, Func<T> body)
        { Effects++; throw new InvalidOperationException("Read-only original attempted an effect."); }
        private sealed class Pin(ReadFence owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            { owner.LivePins--; owner.PinCleanupEntered.TrySetResult(); return owner.PinCleanup is { } raw ? new(raw) : ValueTask.CompletedTask; }
        }
    }
    private sealed class EffectOnlyFence(ReadFence original) : IWorkspaceToolFinalFence
    {
        public TaskRunAttemptAdmission OriginalAttempt => original.OriginalAttempt;
        public Guid ActionId => original.ActionId; public string CanonicalWorkspaceRoot => original.CanonicalWorkspaceRoot; public OllamaToolCall OriginalCall => original.OriginalCall;
        public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token) => original.AcquireOriginalCommitPinAsync(token);
        public void DemandOriginalEffect(string root, WorkspaceToolEffectKind kind, string target, string digest) => original.DemandOriginalEffect(root, kind, target, digest);
        public T RunOriginalEffect<T>(string root, WorkspaceToolEffectKind kind, string target, string digest, Func<T> body) => original.RunOriginalEffect(root, kind, target, digest, body);
    }
}
