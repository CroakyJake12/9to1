using System.Security.Cryptography;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Pass-15 owning profile-aware hardware producer tests. These drive the REAL
/// <see cref="StrataHardwareObservationSource"/> through its trusted installed-profile selection
/// (legacy V1 default vs verified candidate V2) using a synthetic worker-lease source plus, for the
/// genuine compiled candidate, the ACTUAL CUDA-false binary supplied via environment variables. No
/// Home/approved-installation receipt is fabricated here; the hardware probe only needs the trusted
/// profile on the lease. MARKED NOTRUN pending Root's serial SDK allocation (no .NET/native
/// execution in this lane). The synthetic binding fixture proves contract/refusal only — it never
/// fakes Ready/CUDA/model success nor is it mislabelled as product hardware acceptance.</summary>
public sealed class StrataHardwareObservationSourceTests
{
    private const string ZeroDigest64 = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly string NonexistentExecutable =
        Path.Combine(Path.GetTempPath(), "dulche-thunder-nonexistent-candidate-worker");

    /// <summary>A candidate profile whose source identity is NOT the verified candidate commit fails
    /// closed BEFORE any probe is launched (no process started, no worker read). The held lease is
    /// still closed exactly once.</summary>
    [Fact]
    public async Task Candidate_profile_with_foreign_source_fails_closed_before_any_probe_launch()
    {
        var binary = new ProbeBinaryLease(new StrataNativeProfile("candidate", 2, "foreign-source-identity"));
        var source = new ProbeSource(binary); var scope = new ProbeScope();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => ObserveAsync(source, scope));
        var causes = Flatten(error).ToArray();
        Assert.Contains(causes, c => c is UnauthorizedAccessException && c.Message.Contains("only admits the verified", StringComparison.Ordinal));
        Assert.Equal(0, binary.WorkerReads); // never read/launch
        Assert.Equal(1, binary.Closes);
        Assert.All(scope.Raw, task => Assert.True(task.IsCompleted));
    }

    /// <summary>An unknown/foreign profile or ABI fails closed before any probe launch and is never
    /// silently downgraded to the V1 default.</summary>
    [Fact]
    public async Task Unknown_profile_fails_closed_before_any_probe_launch()
    {
        var binary = new ProbeBinaryLease(new StrataNativeProfile("mystery", 9, "whatever"));
        var source = new ProbeSource(binary); var scope = new ProbeScope();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => ObserveAsync(source, scope));
        var causes = Flatten(error).ToArray();
        Assert.Contains(causes, c => c is UnauthorizedAccessException && c.Message.Contains("only admits the verified", StringComparison.Ordinal));
        Assert.Equal(0, binary.WorkerReads);
        Assert.Equal(1, binary.Closes);
        Assert.All(scope.Raw, task => Assert.True(task.IsCompleted));
    }

    /// <summary>The legacy V1 default (null installed profile) is still accepted as the ABI1 path and
    /// proceeds to read the worker executable (the held executable is intentionally absent for the
    /// legacy contract, so the truthful observation is the open failure — not a profile refusal).</summary>
    [Fact]
    public async Task Legacy_V1_default_still_attempts_the_ABI1_probe_after_profile_selection()
    {
        var binary = new ProbeBinaryLease(null);
        var source = new ProbeSource(binary); var scope = new ProbeScope();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => ObserveAsync(source, scope));
        var causes = Flatten(error).ToArray();
        Assert.DoesNotContain(causes, c => c is UnauthorizedAccessException && c.Message.Contains("only admits the verified", StringComparison.Ordinal));
        Assert.Equal(1, binary.WorkerReads); // one owned probe attempted
        Assert.Equal(1, binary.Closes);
        Assert.All(scope.Raw, task => Assert.True(task.IsCompleted));
    }

    /// <summary>End-to-end profile-aware hardware probe against the ACTUAL independently compiled
    /// candidate native V2 binary (6744db6d…): the truthful non-CUDA refusal ("No actual CUDA build
    /// exists") must surface from the protected producer, NOT a legacy ABI mismatch and NOT a fake
    /// Ready. Exactly one worker read proves there is no probe-then-create double launch. Requires
    /// the same environment variables the managed acceptance target uses; NOTRUN until Root's SDK slot
    /// provides them and the compiled binary.</summary>
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Genuine_candidate_binary_reports_truthful_non_CUDA_unavailability()
    {
        var binary = Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_BINARY");
        var digest = Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_SHA256");
        Assert.False(string.IsNullOrWhiteSpace(binary));
        Assert.False(string.IsNullOrWhiteSpace(digest));
        Assert.True(File.Exists(binary));
        Assert.Equal(digest!.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(binary!))));
        var lease = new ProbeBinaryLease(new StrataNativeProfile("candidate", 2, StrataCandidateWorker.VerifiedCandidateSourceCommit))
        { ExecutablePath = binary!, ExpectedSha256 = digest!, PythonPath = "/usr/bin/python3" };
        var source = new ProbeSource(lease); var scope = new ProbeScope();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => ObserveAsync(source, scope));
        var causes = Flatten(error).ToArray();
        Assert.Contains(causes, c => c is InferenceEngineException e && e.Error.Code == DulcheErrorCode.ProviderUnavailable &&
            e.Message.Contains("No actual CUDA build exists", StringComparison.Ordinal));
        Assert.DoesNotContain(causes, c => c.Message.Contains("ABI/origin handshake is invalid", StringComparison.Ordinal));
        Assert.Equal(1, lease.WorkerReads);   // single owned probe, no double launch
        Assert.Equal(1, lease.Closes);
        Assert.All(scope.Raw, task => Assert.True(task.IsCompleted));
    }

    private static Task ObserveAsync(ProbeSource source, ProbeScope scope)
        => new StrataHardwareObservationSource(source).ObserveOriginalAsync(Admission(), scope, CancellationToken.None);

    private static IEnumerable<Exception> Flatten(Exception error)
    {
        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var leaf in Flatten(inner)) yield return leaf;
        }
        else yield return error;
    }

    private sealed class ProbeSource(ProbeBinaryLease binary) : IOriginalStrataWorkerSource
    {
        public Task<StrataOriginalWorkerLease> AcquireOriginalAsync(TaskRunAttemptAdmission admission,
            IInferenceEngineOriginalSourceScope scope, CancellationToken token)
            => Task.FromResult<StrataOriginalWorkerLease>(binary);
        public bool IsIssuedOriginalWorkerLease(StrataOriginalWorkerLease lease, TaskRunAttemptAdmission admission)
            => ReferenceEquals(lease, binary);
    }

    private sealed class ProbeBinaryLease(StrataNativeProfile? profile) : StrataOriginalWorkerLease
    {
        public int WorkerReads, Closes;
        public string ExecutablePath = NonexistentExecutable;
        public string ExpectedSha256 = ZeroDigest64;
        public string? PythonPath;
        public override StrataNativeProfile? InstalledProfile => profile;
        public override StrataBundledWorker OriginalWorker { get { WorkerReads++; return new(ExecutablePath, ExpectedSha256, PythonPath); } }
        public override void DemandCurrentOriginalBinding() { }
        public override ValueTask DisposeAsync() { Closes++; return ValueTask.CompletedTask; }
    }

    private sealed class ProbeScope : IInferenceEngineOriginalSourceScope
    {
        public readonly List<Task> Raw = [];
        public T InvokeOriginalFactory<T>(Func<T> factory) => factory();
        public T InvokeOriginalCleanup<T>(Func<T> cleanup) => cleanup();
        public void RetainOriginalTask(Task raw) { lock (Raw) Raw.Add(raw); }
    }

    private static TaskRunAttemptAdmission Admission()
    {
        var snapshot = new TaskExecutionSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hardware-probe-control",
            TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return new(snapshot, Guid.NewGuid(), new UnissuedLease());
    }
    private sealed class UnissuedLease : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => throw new UnauthorizedAccessException();
        public Guid AttemptId => Guid.Empty; public TaskRunRouteCandidate Candidate => throw new UnauthorizedAccessException();
        public string ReceiptReference => "unissued"; public ValueTask RevalidateAsync(CancellationToken token) => throw new UnauthorizedAccessException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
