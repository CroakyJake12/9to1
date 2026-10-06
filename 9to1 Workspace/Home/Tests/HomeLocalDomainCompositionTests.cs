using System.Reflection;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Home.Tests;

public sealed class HomeLocalDomainCompositionTests
{
    [Fact]
    public async Task Actual_linux_profile_and_aliases_share_one_domain_and_issue_no_native_session()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithDomain(new OperatingSystemPrincipalSource(), async (domain, _) =>
        {
            var original = domain.StartOriginalAsync();
            Assert.Same(original, domain.OriginalStartTask);
            Assert.Same(original, domain.StartOriginalAsync());
            await original.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            domain.DemandOriginalStarted();
            var actor = domain.GetOriginalStartedActor();
            Assert.StartsWith("local-profile:", actor.ActorId);
            Assert.Same(domain.StateStore, domain.Services.GetService(typeof(IHomeCoreStateStore)));
            Assert.Same(domain.Profiles, domain.Services.GetService(typeof(IAuthenticatedResourceActorSource)));
            Assert.Same(domain.Permissions, domain.Services.GetService(typeof(HomePermissionTrustService)));
            Assert.Same(domain.Broker, domain.Services.GetService(typeof(HomeResourceOperationBroker)));
            Assert.True(domain.IsBoundToOriginalComposition(domain.StateStore, domain.Profiles,
                domain.Permissions, domain.Resources, domain.Ownership, domain.Broker));
            Assert.Null(domain.Services.GetService(typeof(HomeNativeCoreApiSessions)));
            Assert.Null(domain.Services.GetService(typeof(IHomeNativeInstalledPeerVerifier)));
            Assert.Null(domain.Services.GetService(typeof(IHomeCoreApi)));
            var current = await domain.Profiles.GetCurrentAsync(CancellationToken.None);
            Assert.Equal(actor, current);
            var read = await domain.StateStore.ReadAsync(CancellationToken.None);
            Assert.True(read.IsSuccess);
            var persisted = Assert.Single(read.State!.Records, record => record.RecordId == "home.local-profile");
            Assert.Equal(HomeDataScope.DeviceLocal, persisted.Scope);
            Assert.Equal(HomeRecordAuthority.LocalCanonical, persisted.Authority);
        });
    }

    [Fact]
    public async Task Real_manual_review_remains_pending_until_an_explicit_decision()
    {
        if (!OperatingSystem.IsLinux()) return;
        await WithDomain(new OperatingSystemPrincipalSource(), async (domain, _) =>
        {
            await domain.StartOriginalAsync().WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            var request = new HomePermissionRequestSubmission(null,
                new("dev:original-control", "Actual test caller", "actual local domain", "reviewed-control", false),
                "original-domain-test-session", new("dev", "dev.workspace.execute", [new("dev.root", "control-root")]),
                new(["dev.root"], 1, [new("dev.root", "control-root")], false));
            var submitted = await domain.Permissions.AuthorizeAsync(request, CancellationToken.None);
            Assert.Equal(HomePermissionRequestState.PendingApproval, submitted.State);
            var snapshot = await domain.Permissions.GetSnapshotAsync(CancellationToken.None);
            var shown = Assert.Single(snapshot.PendingRequests, item => item.RequestId == submitted.RequestId);
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(shown)));
            Assert.False(string.IsNullOrWhiteSpace(digest));
            Assert.False((await domain.Permissions.BeginExecutionAsync(submitted.RequestId, CancellationToken.None)).IsAllowed);
            // This owning control deliberately DECLINES. Production never makes a decision.
            Assert.True((await domain.Permissions.DecideAsync(submitted.RequestId, HomeApprovalChoice.Decline,
                cancellationToken: CancellationToken.None)).Succeeded);
            Assert.Equal(HomePermissionRequestState.Denied,
                (await domain.Permissions.GetAuthorizationAsync(submitted.RequestId, CancellationToken.None)).State);
        }, [new ExecutePolicy()]);
    }

    [Fact]
    public async Task Held_actual_principal_keeps_whole_close_pending_and_seals_disclosure()
    {
        if (!OperatingSystem.IsLinux()) return;
        var source = new HeldPrincipal();
        await WithDomain(source, async (domain, expected) =>
        {
            Task? start = null; Task? close = null; Exception? primary = null;
            List<Exception> cleanup = [];
            try
            {
                start = domain.StartOriginalAsync();
                await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                await AwaitEnrollment(domain, source.Raw.Task);
                close = domain.CloseAndDrainAsync();
                Assert.False(close.IsCompleted);
                Assert.Same(close, domain.CloseAndDrainAsync());
                Assert.Throws<InvalidOperationException>(domain.DemandOriginalStarted);
                Assert.Throws<InvalidOperationException>(() => { _ = domain.GetOriginalStartedActor(); });
            }
            catch (Exception error) { primary = error; }
            finally
            {
                try { source.Raw.TrySetResult(await new OperatingSystemPrincipalSource().GetPrincipalAsync(CancellationToken.None)); }
                catch (Exception error) { cleanup.Add(error); source.Raw.TrySetException(error); }
                if (start is not null) cleanup.AddRange(await ObserveTerminal(start));
                if (close is not null) cleanup.AddRange(await ObserveTerminal(close));
            }
            if (primary is not null)
                throw new AggregateException("The held control body and actual cleanup failed.", new[] { primary }.Concat(cleanup));
            Assert.NotNull(start);
            Assert.NotNull(close);
            Assert.True(start!.IsCanceled);
            Assert.True(close!.IsFaulted);
            Assert.True(start.IsCanceled);
            await DemandOwnedCancellationCauses(domain, cleanup, expected);
        });
    }

    [Fact]
    public async Task Actual_postawait_principal_callback_with_restored_context_cannot_return_existing_close()
    {
        if (!OperatingSystem.IsLinux()) return;
        var captured = ExecutionContext.Capture()!;
        var source = new RestoringPrincipal(captured);
        await WithDomain(source, async (domain, _) =>
        {
            source.Domain = domain;
            var actual = domain.StartOriginalAsync();
            await actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(source.Refusal);
            Assert.IsType<InvalidOperationException>(source.Refusal);
            Assert.False(source.ReturnedClose);
            Assert.Null(source.CloseAtCallback);
            Assert.Null(domain.OriginalCloseTask);
            var close = domain.CloseAndDrainAsync();
            Assert.Same(close, domain.CloseAndDrainAsync());
            await close.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(domain.OriginalProcessRetirementRequestTask);
            Assert.True(domain.OriginalProcessRetirementRequestTask!.IsCompletedSuccessfully);
            Assert.NotNull(domain.OriginalLayoutCloseTask);
            Assert.True(domain.OriginalLayoutCloseTask!.IsCompletedSuccessfully);
            Assert.Throws<InvalidOperationException>(() => { _ = domain.GetOriginalStartedActor(); });
            await Assert.ThrowsAsync<ObjectDisposedException>(() => domain.Profiles.GetCurrentAsync(CancellationToken.None).AsTask());
        });
    }

    [Fact]
    public void Existing_unsafe_Home_parent_is_refused_without_chmod_or_profile_creation()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "home-domain-unsafe-" + Guid.NewGuid().ToString("N"));
        var source = new CountingPrincipal();
        WithTemporaryRoot(root, () =>
        {
            Directory.CreateDirectory(root);
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(root, mode);
            var path = Path.Combine(root, "home-state.json");
            var failure = Assert.Throws<AggregateException>(() => { _ = new HomeLocalDomainComposition(new(path), source); });
            Assert.Contains(Leaves(failure), error => error is UnauthorizedAccessException);
            Assert.Equal(mode, File.GetUnixFileMode(root));
            Assert.False(File.Exists(path));
            Assert.Equal(0, source.Calls);
        });
    }

    [Fact]
    public void Actual_state_symlink_is_refused_before_any_profile_callback()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "home-domain-symlink-" + Guid.NewGuid().ToString("N"));
        var source = new CountingPrincipal();
        WithTemporaryRoot(root, () =>
        {
            Directory.CreateDirectory(root);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var target = Path.Combine(root, "original.json");
            File.WriteAllText(target, "retained-original");
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.CreateSymbolicLink(Path.Combine(root, "home-state.json"), target);
            var failure = Assert.Throws<AggregateException>(() =>
            { _ = new HomeLocalDomainComposition(new(Path.Combine(root, "home-state.json")), source); });
            Assert.Contains(Leaves(failure), error => error is UnauthorizedAccessException);
            Assert.Equal("retained-original", File.ReadAllText(target));
            Assert.Equal(0, source.Calls);
        });
    }

    [Fact]
    public async Task Actual_parent_replacement_during_held_profile_refuses_publication_and_joins_layout_close()
    {
        if (!OperatingSystem.IsLinux()) return;
        var source = new HeldPrincipal();
        await WithDomain(source, async (domain, expected) =>
        {
            var path = (string)typeof(FileHomeCoreStateStore).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(domain.StateStore)!;
            var root = Path.GetDirectoryName(path)!;
            var displaced = root + ".retained";
            Task? actual = null; Task? close = null; Exception? primary = null;
            List<Exception> cleanup = [];
            try
            {
                actual = domain.StartOriginalAsync();
                await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                await AwaitEnrollment(domain, source.Raw.Task);
                Directory.Move(root, displaced);
                Directory.CreateDirectory(root);
                File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                source.Raw.TrySetResult(await new OperatingSystemPrincipalSource().GetPrincipalAsync(CancellationToken.None));
                var refusal = await Assert.ThrowsAsync<AggregateException>(() => actual);
                Assert.True(actual.IsFaulted);
                var cause = Assert.Single(Leaves(refusal).Distinct<Exception>(ReferenceEqualityComparer.Instance));
                Assert.IsType<UnauthorizedAccessException>(cause);
                expected.Add(cause);
                Assert.Throws<InvalidOperationException>(domain.DemandOriginalStarted);
                Assert.False(File.Exists(path));
                close = domain.CloseAndDrainAsync();
                await Assert.ThrowsAsync<AggregateException>(() => close);
                foreach (var error in Leaves(close.Exception!)) Assert.Same(cause, error);
                Assert.NotNull(domain.OriginalLayoutCloseTask);
                Assert.True(domain.OriginalLayoutCloseTask!.IsFaulted);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                source.Raw.TrySetResult("unpublished-cleanup-principal");
                try { close ??= domain.CloseAndDrainAsync(); }
                catch (Exception error) { cleanup.Add(error); }
                if (actual is not null) cleanup.AddRange(await ObserveTerminal(actual));
                if (close is not null) cleanup.AddRange(await ObserveTerminal(close));
                try { if (Directory.Exists(displaced)) Directory.Delete(displaced, true); }
                catch (Exception error) { cleanup.Add(error); }
            }
            var unknown = cleanup.Where(error => !expected.Contains(error)).ToArray();
            if (primary is not null || unknown.Length != 0)
                throw new AggregateException("Parent replacement body/independent cleanup failed.",
                    (primary is null ? Array.Empty<Exception>() : [primary]).Concat(unknown));
        });
    }

    [Fact]
    public async Task Actual_faulted_principal_OCE_and_sibling_remain_faulted_with_same_leaf_references()
    {
        if (!OperatingSystem.IsLinux()) return;
        var cancelled = new OperationCanceledException("actual faulted principal OCE");
        var sibling = new IOException("actual principal sibling");
        var raw = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([cancelled, sibling]);
        await WithDomain(new FixedPrincipal(raw.Task), async (domain, expected) =>
        {
            expected.Add(cancelled); expected.Add(sibling);
            var actual = domain.StartOriginalAsync();
            await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(raw.Task.IsFaulted);
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            Assert.Contains(raw.Task, OriginalTasks(domain));
            Assert.Equal(2, Leaves(actual.Exception!).Distinct<Exception>(ReferenceEqualityComparer.Instance).Count());
            Assert.Contains(cancelled, Leaves(actual.Exception!));
            Assert.Contains(sibling, Leaves(actual.Exception!));
            var close = domain.CloseAndDrainAsync();
            await Assert.ThrowsAsync<AggregateException>(() => close);
            foreach (var cause in Leaves(close.Exception!)) Assert.Contains(cause, expected);
        });
    }

    [Fact]
    public async Task Actual_cancelled_principal_preserves_original_task_status_and_whole_cleanup()
    {
        if (!OperatingSystem.IsLinux()) return;
        var raw = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetCanceled(new CancellationToken(true));
        await WithDomain(new FixedPrincipal(raw.Task), async (domain, expected) =>
        {
            var actual = domain.StartOriginalAsync();
            var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            Assert.True(raw.Task.IsCanceled);
            Assert.True(actual.IsCanceled);
            Assert.Contains(raw.Task, OriginalTasks(domain));
            var close = domain.CloseAndDrainAsync();
            await Assert.ThrowsAsync<AggregateException>(() => close);
            await DemandOwnedCancellationCauses(domain, Leaves(close.Exception!).ToArray(), expected);
            Assert.Contains(cancellation, await ObserveTerminal(actual));
        });
    }

    [Fact]
    public async Task Parent_postcallback_fault_retains_and_joins_the_same_held_principal_original()
    {
        if (!OperatingSystem.IsLinux()) return;
        var source = new HeldPrincipal();
        var injected = new OperationCanceledException("actual parent postcallback fault");
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenRaw = 0; var thrown = 0;
        var parentOriginals = new List<Task>();
        void Retain(Task task)
        {
            lock (parentOriginals) parentOriginals.Add(task);
            if (ReferenceEquals(task, source.Raw.Task))
            { Volatile.Write(ref seenRaw, 1); enrolled.TrySetResult(); }
        }
        void Scope(Action callback)
        {
            callback();
            if (Volatile.Read(ref seenRaw) != 0 && Interlocked.CompareExchange(ref thrown, 1, 0) == 0)
                throw injected;
        }
        await WithDomain(source, async (domain, expected) =>
        {
            expected.Add(injected);
            Task? actual = null; Task? close = null; Exception? primary = null;
            List<Exception> cleanup = [];
            try
            {
                actual = domain.StartOriginalAsync();
                await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                await AwaitEnrollment(domain, source.Raw.Task);
                Assert.False(actual.IsCompleted);
                lock (parentOriginals) Assert.Contains(source.Raw.Task, parentOriginals);
                source.Raw.TrySetResult(await new OperatingSystemPrincipalSource().GetPrincipalAsync(CancellationToken.None));
                await Assert.ThrowsAsync<AggregateException>(() => actual);
                Assert.True(actual.IsFaulted);
                Assert.False(actual.IsCanceled);
                foreach (var cause in Leaves(actual.Exception!)) Assert.Same(injected, cause);
                Assert.Throws<InvalidOperationException>(domain.DemandOriginalStarted);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                source.Raw.TrySetResult("unpublished-parent-cleanup-principal");
                try { close = domain.CloseAndDrainAsync(); } catch (Exception error) { cleanup.Add(error); }
                if (actual is not null) cleanup.AddRange(await ObserveTerminal(actual));
                if (close is not null) cleanup.AddRange(await ObserveTerminal(close));
                Task[] captured; lock (parentOriginals) captured = parentOriginals.ToArray();
                foreach (var raw in captured) cleanup.AddRange(await ObserveTerminal(raw));
            }
            var unknown = cleanup.Where(cause => !expected.Contains(cause)).ToArray();
            if (primary is not null || unknown.Length != 0)
                throw new AggregateException("Parent source body/independent raw cleanup failed.",
                    (primary is null ? Array.Empty<Exception>() : [primary]).Concat(unknown));
        }, originalScope: Scope, retainRaw: Retain);
    }

    [Fact]
    public async Task Swallowed_parent_repeat_refusal_remains_faulted_and_cannot_publish_an_actor()
    {
        if (!OperatingSystem.IsLinux()) return;
        Exception? refused = null;
        var attempted = 0;
        void Scope(Action callback)
        {
            callback();
            if (Interlocked.CompareExchange(ref attempted, 1, 0) != 0) return;
            try { callback(); } catch (Exception error) { refused = error; }
        }
        await WithDomain(new OperatingSystemPrincipalSource(), async (domain, expected) =>
        {
            Task? actual = null; Task? close = null; Exception? primary = null;
            List<Exception> cleanup = [];
            try
            {
                var synchronous = Assert.Throws<AggregateException>(() => { _ = domain.StartOriginalAsync(); });
                Assert.NotNull(refused);
                Assert.IsType<InvalidOperationException>(refused);
                expected.Add(refused!);
                foreach (var cause in Leaves(synchronous)) Assert.Same(refused, cause);
                actual = domain.OriginalStartTask;
                Assert.NotNull(actual);
                await Assert.ThrowsAsync<AggregateException>(() => actual!);
                Assert.True(actual!.IsFaulted);
                Assert.False(actual.IsCanceled);
                foreach (var cause in Leaves(actual.Exception!)) Assert.Same(refused, cause);
                Assert.Throws<InvalidOperationException>(domain.DemandOriginalStarted);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                actual ??= domain.OriginalStartTask;
                try { close = domain.CloseAndDrainAsync(); } catch (Exception error) { cleanup.Add(error); }
                if (actual is not null) cleanup.AddRange(await ObserveTerminal(actual));
                if (close is not null) cleanup.AddRange(await ObserveTerminal(close));
            }
            var unknown = cleanup.Where(cause => !expected.Contains(cause)).ToArray();
            if (primary is not null || unknown.Length != 0)
                throw new AggregateException("Parent repeat body/independent cleanup failed.",
                    (primary is null ? Array.Empty<Exception>() : [primary]).Concat(unknown));
        }, originalScope: Scope, retainRaw: _ => { });
    }

    [Fact]
    public async Task Restored_postawait_principal_cannot_obtain_the_same_pending_startup_driver()
    {
        if (!OperatingSystem.IsLinux()) return;
        var source = new RestoringStartupPrincipal(ExecutionContext.Capture()!);
        await WithDomain(source, async (domain, _) =>
        {
            source.Domain = domain;
            var actual = domain.StartOriginalAsync();
            await actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(source.Refusal);
            Assert.IsType<InvalidOperationException>(source.Refusal);
            Assert.Null(source.ReturnedOriginal);
            Assert.Same(actual, domain.OriginalStartTask);
            Assert.True(actual.IsCompletedSuccessfully);
            Assert.Same(actual, domain.StartOriginalAsync());
            domain.DemandOriginalStarted();
        });
    }

    private sealed class RestoringStartupPrincipal(ExecutionContext captured) : ITrustedHostPrincipalSource
    {
        internal HomeLocalDomainComposition? Domain;
        internal Exception? Refusal;
        internal Task? ReturnedOriginal;
        private int _calls;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 2)
                ExecutionContext.Run(captured, _ =>
                {
                    try { ReturnedOriginal = Domain!.StartOriginalAsync(); }
                    catch (Exception error) { Refusal = error; }
                }, null);
            return new OperatingSystemPrincipalSource().GetPrincipalAsync(cancellationToken);
        }
    }

    private static async Task DemandOwnedCancellationCauses(HomeLocalDomainComposition domain,
        IReadOnlyList<Exception> causes, HashSet<Exception> expected)
    {
        var actualTasks = OriginalTasks(domain).Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        var cancelled = actualTasks.Where(task => task.IsCanceled).ToArray();
        Assert.NotEmpty(cancelled);
        Assert.DoesNotContain(actualTasks, task => task.IsFaulted);
        var observed = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        foreach (var actual in cancelled)
            foreach (var leaf in await ObserveTerminal(actual)) observed.Add(leaf);
        foreach (var cause in causes)
        {
            // Async wrappers may preserve an exact cancellation exception; an original
            // SetCanceled Task may create a new TCE at each await. Its positive Task
            // identity must name the SAME retained cancelled original, never a sibling.
            if (!observed.Contains(cause))
            {
                var taskCause = Assert.IsType<TaskCanceledException>(cause);
                Assert.NotNull(taskCause.Task);
                Assert.Contains(taskCause.Task!, cancelled);
                Assert.True(taskCause.Task!.IsCanceled);
            }
            Assert.IsAssignableFrom<OperationCanceledException>(cause);
            expected.Add(cause);
        }
    }

    private sealed class ExecutePolicy : IHomeActionPolicySource
    {
        public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
            appId == "dev" && actionId == "dev.workspace.execute" ? new(HomePermissionRisk.High, false, false) : null;
    }
    private sealed class FixedPrincipal(Task<string?> raw) : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken) => new(raw); }
    private sealed class CountingPrincipal : ITrustedHostPrincipalSource
    {
        internal int Calls;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        { Calls++; return new OperatingSystemPrincipalSource().GetPrincipalAsync(cancellationToken); }
    }
    private sealed class HeldPrincipal : ITrustedHostPrincipalSource
    {
        internal readonly TaskCompletionSource<string?> Raw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1) { Entered.TrySetResult(); return new(Raw.Task); }
            return new OperatingSystemPrincipalSource().GetPrincipalAsync(CancellationToken.None);
        }
    }
    private sealed class RestoringPrincipal(ExecutionContext captured) : ITrustedHostPrincipalSource
    {
        internal HomeLocalDomainComposition? Domain;
        internal Exception? Refusal;
        internal Task? CloseAtCallback;
        internal bool ReturnedClose;
        private int _calls;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 2)
                ExecutionContext.Run(captured, _ =>
                {
                    try { _ = Domain!.CloseAndDrainAsync(); ReturnedClose = true; }
                    catch (Exception error) { Refusal = error; }
                    CloseAtCallback = Domain!.OriginalCloseTask;
                }, null);
            return new OperatingSystemPrincipalSource().GetPrincipalAsync(cancellationToken);
        }
    }
    private static Task[] OriginalTasks(HomeLocalDomainComposition domain)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var gate = typeof(HomeLocalDomainComposition).GetField("_gate", flags)!.GetValue(domain)!;
        lock (gate)
        {
            var startup = typeof(HomeLocalDomainComposition).GetField("_startup", flags)!.GetValue(domain)!;
            var type = startup.GetType();
            var tasks = ((IEnumerable<Task>)type.GetField("Sources", flags)!.GetValue(startup)!).ToList();
            tasks.Add((Task)type.GetField("Driver", flags)!.GetValue(startup)!);
            return tasks.ToArray();
        }
    }
    private static async Task AwaitEnrollment(HomeLocalDomainComposition domain, Task raw)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!OriginalTasks(domain).Contains(raw, ReferenceEqualityComparer.Instance))
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The actual principal task was not enrolled.");
            await Task.Delay(1, CancellationToken.None);
        }
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [error];
    private static async Task<Exception[]> ObserveTerminal(Task actual)
    {
        try { await actual; return []; }
        catch (Exception error)
        {
            return (actual.IsFaulted ? Leaves(actual.Exception!) : Leaves(error)).ToArray();
        }
    }
    private static async Task WithDomain(ITrustedHostPrincipalSource principals,
        Func<HomeLocalDomainComposition, HashSet<Exception>, Task> body, IHomeActionPolicySource[]? policies = null,
        Action<Action>? originalScope = null, Action<Task>? retainRaw = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "home-original-linux-domain-" + Guid.NewGuid().ToString("N"));
        HomeLocalDomainComposition? domain = null; Task? actualBody = null; Task? close = null;
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        List<Exception> failures = [];
        try
        {
            Directory.CreateDirectory(root);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            domain = new(new(Path.Combine(root, "home-state.json")), principals, originalActionPolicies: policies,
                originalSynchronousScope: originalScope, retainOriginalTask: retainRaw);
            actualBody = body(domain, expected);
            await actualBody;
        }
        catch (Exception error) { Keep(actualBody, error); }
        finally
        {
            try { if (domain is not null) close = domain.CloseAndDrainAsync(); } catch (Exception error) { Keep(null, error); }
            if (close is not null)
                try { await close; }
                catch (Exception error) { Keep(close, error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { Keep(null, error); }
        }
        if (failures.Count != 0) throw new AggregateException("Domain control body or independent cleanup failed.", failures);
        void Keep(Task? actual, Exception error)
        {
            var causes = actual?.IsFaulted == true ? Leaves(actual.Exception!) : Leaves(error);
            foreach (var cause in causes)
                if (!expected.Contains(cause) && !failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause);
        }
    }
    private static void WithTemporaryRoot(string root, Action body)
    {
        List<Exception> errors = [];
        try { body(); } catch (Exception error) { errors.AddRange(Leaves(error)); }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception error) { errors.AddRange(Leaves(error)); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual root control body/independent cleanup failed.", errors);
    }
}
