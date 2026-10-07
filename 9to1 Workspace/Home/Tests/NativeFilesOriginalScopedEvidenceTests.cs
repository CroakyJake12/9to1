using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class NativeFilesOriginalScopedEvidenceTests
{
    [Fact]
    public async Task Held_actual_principal_is_enrolled_before_parent_scope_fault_and_independently_joined()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var injected = new OperationCanceledException("actual Files parent postcallback fault");
            rig.Expected.Add(injected); rig.Principal.Arm();
            var failed = 0;
            void Scope(Action callback)
            {
                callback();
                if (rig.RawEnrolled.Task.IsCompleted && Interlocked.CompareExchange(ref failed, 1, 0) == 0)
                    throw injected;
            }
            var actual = rig.Read(Scope);
            await rig.RawEnrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.False(actual.IsCompleted);
            Assert.Contains(rig.Principal.Raw.Task, rig.Originals());
            rig.Principal.Release();
            await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            foreach (var cause in Leaves(actual.Exception!)) Assert.Same(injected, cause);
            Assert.True(rig.Principal.Raw.Task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task Swallowed_repeated_parent_callback_refusal_is_retained_without_a_second_factory()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            rig.Principal.Arm(); rig.Principal.Release();
            Exception? refused = null; var once = 0;
            void Scope(Action callback)
            {
                callback();
                if (Interlocked.CompareExchange(ref once, 1, 0) != 0) return;
                try { callback(); } catch (Exception error) { refused = error; rig.Expected.Add(error); }
            }
            var actual = rig.Read(Scope);
            await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.NotNull(refused);
            Assert.IsType<InvalidOperationException>(refused);
            foreach (var cause in Leaves(actual.Exception!)) Assert.Same(refused, cause);
            Assert.Equal(1, rig.Principal.ArmedCalls);
            Assert.Contains(rig.Principal.Raw.Task, rig.Originals());
            Assert.True(actual.IsFaulted);
        });
    }

    [Fact]
    public async Task Actual_faulted_principal_OCE_and_IO_sibling_are_preserved_by_scoped_evidence()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            var cancelled = new OperationCanceledException("actual faulted Files principal OCE");
            var sibling = new IOException("actual Files principal sibling");
            rig.Expected.Add(cancelled); rig.Expected.Add(sibling);
            rig.Principal.Arm(); rig.Principal.Raw.SetException([cancelled, sibling]);
            var actual = rig.Read(callback => callback());
            await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(rig.Principal.Raw.Task.IsFaulted);
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            Assert.Contains(rig.Principal.Raw.Task, rig.Originals());
            Assert.Equal(2, Leaves(actual.Exception!).Distinct<Exception>(ReferenceEqualityComparer.Instance).Count());
            Assert.Contains(cancelled, Leaves(actual.Exception!));
            Assert.Contains(sibling, Leaves(actual.Exception!));
        });
    }

    [Fact]
    public async Task Actually_cancelled_original_read_keeps_task_status_and_own_cancellation_provenance()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithRig(async rig =>
        {
            rig.Principal.Arm(); rig.Principal.Raw.SetCanceled(new CancellationToken(true));
            var actual = rig.Read(callback => callback());
            var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            rig.Expected.Add(observed);
            rig.AllowActualCancellation = true;
            Assert.True(rig.Principal.Raw.Task.IsCanceled);
            Assert.True(actual.IsCanceled);
            Assert.False(actual.IsFaulted);
            Assert.Contains(rig.Principal.Raw.Task, rig.Originals());
        });
    }

    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal readonly TaskCompletionSource<string?> Raw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _actualPrincipal;
        private int _armed;
        internal int ArmedCalls;
        internal async Task Initialize()
            => _actualPrincipal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(CancellationToken.None);
        internal void Arm() => Volatile.Write(ref _armed, 1);
        internal void Release() => Raw.TrySetResult(_actualPrincipal);
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _armed) != 0)
            { Interlocked.Increment(ref ArmedCalls); return new(Raw.Task); }
            return new OperatingSystemPrincipalSource().GetPrincipalAsync(cancellationToken);
        }
    }
    private sealed class Rig
    {
        internal readonly Principal Principal = new();
        internal readonly TaskCompletionSource RawEnrolled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly HashSet<Exception> Expected = new(ReferenceEqualityComparer.Instance);
        internal bool AllowActualCancellation;
        internal HomeLocalDomainComposition? Domain;
        internal NativeFilesWorkspaceService? Workspaces;
        internal Task? Startup;
        internal Task? ActualRead;
        private readonly List<Task> _raw = [];
        internal Task[] Originals() { lock (_raw) return _raw.ToArray(); }
        internal Task<HomeLocalStoreEvidence?> Read(Action<Action> scope)
        {
            var actual = Workspaces!.ReadWithinOriginalSourceAsync(Guid.NewGuid().ToString("D"), scope, task =>
            {
                lock (_raw) _raw.Add(task);
                if (ReferenceEquals(task, Principal.Raw.Task)) RawEnrolled.TrySetResult();
            }, CancellationToken.None).AsTask();
            ActualRead = actual; return actual;
        }
        internal async Task<Exception[]> Close()
        {
            Principal.Release();
            List<Exception> errors = [];
            foreach (var actual in new[] { ActualRead }.Where(task => task is not null).Cast<Task>().Concat(Originals()).Distinct<Task>(ReferenceEqualityComparer.Instance))
                await Join(actual);
            Task? close = null;
            try { if (Domain is not null) close = Domain.CloseAndDrainAsync(); } catch (Exception error) { Keep(null, error); }
            if (close is not null) await Join(close);
            if (Startup is not null) await Join(Startup);
            return errors.ToArray();
            async Task Join(Task actual)
            {
                try { await actual; } catch (Exception error) { Keep(actual, error); }
            }
            void Keep(Task? actual, Exception caught)
            {
                foreach (var cause in actual?.IsFaulted == true ? Leaves(actual.Exception!) : Leaves(caught))
                {
                    if (Expected.Contains(cause)) continue;
                    if (AllowActualCancellation && actual?.IsCanceled == true && cause is TaskCanceledException cancelled &&
                        cancelled.Task is { IsCanceled: true } owner &&
                        (ReferenceEquals(owner, actual) || Originals().Contains(owner, ReferenceEqualityComparer.Instance))) continue;
                    if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
                }
            }
        }
    }
    private static async Task WithRig(Func<Rig, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "home-files-original-scoped-" + Guid.NewGuid().ToString("N"));
        var rig = new Rig(); Task? actualBody = null; List<Exception> failures = [];
        try
        {
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original local Home fixture requires Linux.");
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await rig.Principal.Initialize();
            rig.Domain = new(new(Path.Combine(root, "home-state.json")), rig.Principal,
                configureOriginalStores: identity =>
                {
                    rig.Workspaces = new(identity.StateStore, identity.Profiles);
                    return new([rig.Workspaces], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = rig.Workspaces });
                });
            rig.Startup = rig.Domain.StartOriginalAsync();
            await rig.Startup.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            actualBody = body(rig); await actualBody;
        }
        catch (Exception error)
        { failures.AddRange(actualBody?.IsFaulted == true ? Leaves(actualBody.Exception!) : Leaves(error)); }
        finally
        {
            // Close the SAME borrowed read cohort before global domain/layout retirement.
            try { failures.AddRange(await rig.Close()); } catch (Exception error) { failures.AddRange(Leaves(error)); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { failures.AddRange(Leaves(error)); }
        }
        if (failures.Count != 0) throw new AggregateException("Actual scoped Files body/independent original cleanup failed.", failures);
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [error];
}
