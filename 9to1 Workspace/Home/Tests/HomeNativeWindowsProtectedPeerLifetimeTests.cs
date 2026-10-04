using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Managed original-task/cleanup contracts only. These cases do not authenticate
// a publisher, protected Windows installation, real process or launch authority.
public sealed class HomeNativeWindowsProtectedPeerLifetimeTests
{
    [Fact]
    public async Task Unavailable_result_cannot_hide_original_close_failure()
    {
        var close = new IOException("same original close");
        var observed = await Assert.ThrowsAsync<IOException>(() =>
            HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync<object>(
                () => ValueTask.FromResult<object?>(null), () => throw close).AsTask());
        Assert.Same(close, observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refusal_and_independent_close_keep_both_original_objects(bool ioRefusal)
    {
        Exception refusal = ioRefusal ? new IOException("original evidence read")
            : new UnauthorizedAccessException("original ACL refusal");
        var close = new IOException("original release");
        var observed = await Assert.ThrowsAsync<AggregateException>(() =>
            HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync<object>(
                () => ValueTask.FromException<object?>(refusal), () => throw close).AsTask());
        Assert.Equal(2, observed.InnerExceptions.Count);
        Assert.Same(refusal, observed.InnerExceptions[0]);
        Assert.Same(close, observed.InnerExceptions[1]);
    }

    [Fact]
    public async Task Cleanup_only_IO_from_native_helper_is_not_ordinary_refusal()
    {
        var release = new IOException("same LocalFree failure");
        var observed = await Assert.ThrowsAsync<IOException>(() =>
            HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync<object>(() =>
            {
                HomeNativeWindowsOriginalEvidenceLifetime.RunWithCleanup(() => { }, () => throw release);
                return ValueTask.FromResult<object?>(new object());
            }, () => { }).AsTask());
        Assert.Same(release, observed);
    }

    [Fact]
    public async Task Unexpected_body_and_close_are_retained_independently()
    {
        var body = new InvalidOperationException("original body");
        var close = new IOException("original close");
        var observed = await Assert.ThrowsAsync<AggregateException>(() =>
            HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync<object>(
                () => ValueTask.FromException<object?>(body), () => throw close).AsTask());
        Assert.Same(body, observed.InnerExceptions[0]);
        Assert.Same(close, observed.InnerExceptions[1]);
    }

    [Fact]
    public async Task Original_body_finally_settles_before_close_or_unavailable_publication()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bodySettled = false;
        var closeStarted = false;
        Task<object?>? original = null;
        Exception? primary = null;
        var cleanup = new List<Exception>();
        try
        {
            original = HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync<object>(async () =>
            {
                entered.SetResult();
                try { await release.Task.ConfigureAwait(false); return null; }
                finally { bodySettled = true; }
            }, () => { closeStarted = true; Assert.True(bodySettled); }).AsTask();
            await entered.Task;
            Assert.False(original.IsCompleted);
            Assert.False(closeStarted);
            Assert.False(bodySettled);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            release.TrySetResult();
            if (original is not null)
                try { await original; } catch (Exception error) { cleanup.Add(error); }
        }
        if (primary is not null) cleanup.Insert(0, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException(cleanup);
        Assert.True(bodySettled);
        Assert.True(closeStarted);
        Assert.Null(await original!);
    }

    [Fact]
    public async Task ACL_body_and_every_original_release_survive_first_release_fault()
    {
        var body = new UnauthorizedAccessException("same ACL body");
        var first = new IOException("first original release");
        var second = new IOException("second original release");
        var attempted = new List<int>();
        var observed = await Assert.ThrowsAsync<AggregateException>(() =>
            HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync<object>(() =>
            {
                HomeNativeWindowsOriginalEvidenceLifetime.RunWithCleanups(() => throw body,
                    () => { attempted.Add(1); throw first; },
                    () => { attempted.Add(2); throw second; });
                return ValueTask.FromResult<object?>(null);
            }, () => attempted.Add(3)).AsTask());
        Assert.Equal(new[] { 1, 2, 3 }, attempted);
        Assert.Equal(3, observed.InnerExceptions.Count);
        Assert.Same(body, observed.InnerExceptions[0]);
        Assert.Same(first, observed.InnerExceptions[1]);
        Assert.Same(second, observed.InnerExceptions[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Null_borrowed_trust_pointer_refuses_before_next_native_helper(int stage)
    {
        var called = new List<int>();
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            HomeNativeWindowsOriginalEvidenceLifetime.FollowBorrowedTrustChain(
                stage == 0 ? IntPtr.Zero : new IntPtr(10),
                _ => { called.Add(1); return stage == 1 ? IntPtr.Zero : new IntPtr(20); },
                _ => { called.Add(2); return stage == 2 ? IntPtr.Zero : new IntPtr(30); },
                _ => { called.Add(3); return stage == 3 ? IntPtr.Zero : new IntPtr(40); });
        });
        Assert.Equal(Enumerable.Range(1, stage), called);
    }

    [Fact]
    public void Borrowed_chain_passes_only_each_same_predecessor_pointer()
    {
        var called = new List<int>();
        var certificate = HomeNativeWindowsOriginalEvidenceLifetime.FollowBorrowedTrustChain(
            new IntPtr(10),
            state => { Assert.Equal(new IntPtr(10), state); called.Add(1); return new IntPtr(20); },
            provider => { Assert.Equal(new IntPtr(20), provider); called.Add(2); return new IntPtr(30); },
            signer => { Assert.Equal(new IntPtr(30), signer); called.Add(3); return new IntPtr(40); });
        Assert.Equal(new IntPtr(40), certificate);
        Assert.Equal(new[] { 1, 2, 3 }, called);
    }

    [Fact]
    public async Task Missing_receipt_owner_cannot_authenticate_observational_tuple()
    {
        var descriptor = new byte[] { 1, 2 };
        var receipt = new byte[] { 3, 4 };
        var entry = new byte[] { 5, 6 };
        var services = new HashSet<string> { "home.core" };
        var roles = new HashSet<string> { "home.session-host" };
        var observation = new HomeNativeWindowsProtectedInstallationReceiptObservation(
            descriptor, receipt, entry, 1, "windows-sid:S-1-5-18", "fixture-only-start",
            "fixture-only-root", "descriptor", "receipt", 7, Guid.NewGuid(),
            new AuthenticatedResourceActor("fixture-only", "fixture-only", null, null, "fixture-only"),
            new HomeNativeInstalledPeer("fixture-only", Guid.NewGuid(), "fixture-only", "fixture-only", services)
                { Roles = roles });
        descriptor[0] = receipt[0] = entry[0] = 99;
        services.Clear(); roles.Clear();
        Assert.Equal(new byte[] { 1, 2 }, observation.CopySignedDescriptor().ToArray());
        Assert.Equal(new byte[] { 3, 4 }, observation.CopySignedReceipt().ToArray());
        Assert.Equal(new byte[] { 5, 6 }, observation.CopyCanonicalPackageEntry().ToArray());
        Assert.Contains("home.core", observation.InstalledObservation.AllowedServiceIds);
        Assert.Contains("home.session-host", observation.InstalledObservation.Roles);
        var returned = observation.CopySignedReceipt().ToArray(); returned[0] = 99;
        Assert.Equal((byte)3, observation.CopySignedReceipt().Span[0]);
        var unavailable = new UnavailableHomeNativeWindowsProtectedInstallationReceiptAuthority();
        Assert.False(await unavailable.IsCurrentAsync(observation, CancellationToken.None));
    }

    [Fact]
    public void Windows_ABI_matches_the_exact_managed_native_structure_layouts()
    {
        // Explicitly Windows-only layout assertion; no native trust call is made.
        if (!OperatingSystem.IsWindows()) return;
        HomeNativeWindowsProtectedPeerEvidence.RequireNativeAbi();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_refusal_and_close_object_is_retained_exactly_once(bool ioRefusal)
    {
        Exception same = ioRefusal ? new IOException("same original body and release")
            : new UnauthorizedAccessException("same original body and release");
        var observed = await Record.ExceptionAsync(() =>
            HomeNativeWindowsOriginalEvidenceLifetime.VerifyAsync<object>(
                () => ValueTask.FromException<object?>(same), () => throw same).AsTask());
        Assert.Same(same, observed);
        Assert.IsNotType<AggregateException>(observed);
    }

    [Fact]
    public async Task Genuine_original_lease_retirement_during_profile_read_refuses_after_the_await()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-verifier-lease-" + Guid.NewGuid().ToString("N"));
        HomeNativeSessionLease? lease = null;
        Task? original = null;
        CancellationTokenSource? deadline = null;
        var principal = new HoldingPrincipal();
        Exception? expected = null;
        Exception? primary = null;
        var cleanup = new List<Exception>();
        try
        {
            deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Directory.CreateDirectory(root);
            var profiles = new HomeLocalProfileIdentity(
                new FileHomeCoreStateStore(Path.Combine(root, "profile.json")), principal);
            var actor = await profiles.GetCurrentAsync(deadline.Token)
                ?? throw new InvalidOperationException("The actual isolated profile is unavailable.");
            lease = await HomeNativeSessionLease.TryAcquireAsync(profiles, new OriginalPaths(root), deadline.Token)
                ?? throw new InvalidOperationException("The actual isolated lease is unavailable.");
            var identity = lease.LeaseIdentity;
            principal.HoldNextRead();
            original = HomeNativeWindowsOriginalEvidenceLifetime.RequireOriginalActorAsync(
                profiles, lease, identity, actor, deadline.Token).AsTask();
            await principal.Entered.Task.WaitAsync(deadline.Token);
            Assert.False(original.IsCompleted);
            Assert.True(lease.IsHeld);
            lease.Dispose();
            principal.Release.TrySetResult();
            expected = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original!);
            Assert.False(lease.IsHeld);
            Assert.Equal(identity, lease.LeaseIdentity);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            principal.Release.TrySetResult();
            if (original is not null)
                try { await original; }
                catch (Exception error) { if (!ReferenceEquals(expected, error)) cleanup.Add(error); }
            try { lease?.Dispose(); } catch (Exception error) { cleanup.Add(error); }
            try { deadline?.Dispose(); } catch (Exception error) { cleanup.Add(error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception error) { cleanup.Add(error); }
        }
        ThrowFixtureOriginals(primary, cleanup);
    }

    [Fact]
    public async Task Launch_retirement_during_final_receipt_await_prevents_publication()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launchCurrent = true;
        var launchChecks = 0;
        var actorChecks = 0;
        var physicalChecks = 0;
        Task<bool>? original = null;
        Exception? primary = null;
        var cleanup = new List<Exception>();
        try
        {
            original = HomeNativeWindowsOriginalEvidenceLifetime.RequireFinalCurrentAsync(async () =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return true; // Managed ordering fixture only; no protected receipt is authenticated.
            }, () => { launchChecks++; return ValueTask.FromResult(launchCurrent); },
                () => { actorChecks++; return ValueTask.CompletedTask; },
                () => physicalChecks++).AsTask();
            await entered.Task;
            Assert.False(original.IsCompleted);
            Assert.Equal(0, launchChecks);
            launchCurrent = false;
            release.TrySetResult();
            Assert.False(await original);
            Assert.Equal(1, launchChecks);
            Assert.Equal(0, actorChecks);
            Assert.Equal(0, physicalChecks);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            release.TrySetResult();
            if (original is not null)
                try { await original; } catch (Exception error) { cleanup.Add(error); }
        }
        ThrowFixtureOriginals(primary, cleanup);
    }

    private sealed class HoldingPrincipal : ITrustedHostPrincipalSource
    {
        private bool _hold;
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void HoldNextRead() => _hold = true;
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_hold)
            {
                _hold = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            return "explicit-isolated-profile-fixture-only";
        }
    }

    private sealed class OriginalPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }

    private static void ThrowFixtureOriginals(Exception? primary, List<Exception> cleanup)
    {
        if (primary is not null && !cleanup.Any(error => ReferenceEquals(error, primary)))
            cleanup.Insert(0, primary);
        var originals = cleanup.Distinct(ReferenceEqualityComparer.Instance).ToArray();
        if (originals.Length == 1) ExceptionDispatchInfo.Capture((Exception)originals[0]).Throw();
        if (originals.Length > 1)
            throw new AggregateException("Original verifier fixture and independent cleanup failed.",
                originals.Cast<Exception>());
    }
}
