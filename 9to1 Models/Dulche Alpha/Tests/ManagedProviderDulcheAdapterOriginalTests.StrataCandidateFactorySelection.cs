using System.Security.Cryptography;
using System.IO;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Pass-14 production candidate-selection seam. These drive the REAL
/// <see cref="StrataManagedInferenceEngineFactory"/> through its profile gate using trusted-source
/// fixtures plus a minimal finite scope; they exercise the actual selection and cleanup, NOT
/// source-string mirrors. MARKED NOTRUN pending Root's serial SDK allocation (no dotnet/native
/// execution in this lane). The synthetic binding fixture proves contract/refusal only — it never
/// fakes Ready/CUDA/model success nor is it mislabelled as product hardware acceptance.</summary>
public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
    private const string ZeroDigest64 = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly string NonexistentExecutable =
        Path.Combine(Path.GetTempPath(), "dulche-thunder-nonexistent-candidate-worker");

    /// <summary>Legacy default (no explicit candidate trusted metadata) selects the V1 worker; it
    /// refuses a GGUF model at the V1 Safetensors-only gate. This proves the default is NOT silently
    /// redirected to V2 and that GGUF is not relabelled to Safetensors.</summary>
    [Fact]
    public Task Legacy_V1_profile_is_default_and_refuses_GGUF_at_the_V1_gate()
        => StrataSelect((h, model) => new ProfileControlledSources(h.Admission, model,
            profile: null, format: "GGUF"), (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is NotSupportedException && c.Message.Contains(
                "preserves the original Safetensors registration", StringComparison.Ordinal));
            Assert.DoesNotContain(causes, c => c.Message.Contains("admits only", StringComparison.Ordinal));
            Assert.Equal(1, sources.Binary.WorkerReads);
            Assert.Equal(1, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });

    /// <summary>Legacy V1 default still admits its native Safetensors format and proceeds past the
    /// gate toward the actual worker start (the held executable is intentionally absent, so the
    /// observation is the open failure — not a format refusal).</summary>
    [Fact]
    public Task Legacy_V1_profile_still_admits_Safetensors_and_proceeds_to_worker_start()
        => StrataSelect((h, model) => new ProfileControlledSources(h.Admission, model,
            profile: null, format: "Safetensors"), (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is FileNotFoundException);
            Assert.DoesNotContain(causes, c => c.Message.Contains("preserves the original Safetensors registration", StringComparison.Ordinal));
            Assert.DoesNotContain(causes, c => c.Message.Contains("admits only", StringComparison.Ordinal));
            Assert.Equal(1, sources.Binary.WorkerReads);
            Assert.Equal(1, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });

    /// <summary>Verified candidate profile selects the V2 worker through ONE create path: it admits the
    /// engine-native GGUF model past the V2 gate and then proceeds to open the held executable (absent
    /// here, so the truthful observation is the open failure). Exactly one worker read proves there is
    /// no probe-before-create double launch.</summary>
    [Fact]
    public Task Verified_candidate_profile_selects_V2_single_create_path_without_double_probe()
        => StrataSelect((h, model) => new ProfileControlledSources(h.Admission, model,
            new StrataNativeProfile("candidate", 2, StrataCandidateWorker.VerifiedCandidateSourceCommit), format: "GGUF"),
            (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is FileNotFoundException);
            Assert.DoesNotContain(causes, c => c.Message.Contains("preserves the original Safetensors registration", StringComparison.Ordinal));
            Assert.DoesNotContain(causes, c => c is NotSupportedException && c.Message.Contains("admits only", StringComparison.Ordinal));
            Assert.Equal(1, sources.Binary.WorkerReads);
            Assert.Equal(1, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });

    /// <summary>The V2 candidate admits ONLY its verified engine-native GGUF registration. A
    /// Safetensors model is refused at the V2 gate — no silent relabelling, no fallback to the legacy
    /// Safetensors path for a candidate install.</summary>
    [Fact]
    public Task Verified_candidate_V2_refuses_Safetensors_without_relabelling()
        => StrataSelect((h, model) => new ProfileControlledSources(h.Admission, model,
            new StrataNativeProfile("candidate", 2, StrataCandidateWorker.VerifiedCandidateSourceCommit), format: "Safetensors"),
            (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is NotSupportedException && c.Message.Contains("admits only", StringComparison.Ordinal));
            Assert.DoesNotContain(causes, c => c.Message.Contains("preserves the original Safetensors registration", StringComparison.Ordinal));
            Assert.Equal(1, sources.Binary.WorkerReads);
            Assert.Equal(1, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });

    /// <summary>A candidate profile whose source identity does NOT equal the verified pinned candidate
    /// source fails closed before any create and before any model acquisition; the held binary is still
    /// closed exactly once.</summary>
    [Fact]
    public Task Candidate_with_unverified_source_identity_fails_closed_before_create()
        => StrataSelect((h, model) => new ProfileControlledSources(h.Admission, model,
            new StrataNativeProfile("candidate", 2, "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef"), format: "GGUF"),
            (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is InferenceEngineException && c.Message.Contains("does not match the verified", StringComparison.Ordinal));
            Assert.Equal(0, sources.Binary.WorkerReads);
            Assert.Equal(0, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });

    /// <summary>An unknown/foreign profile or ABI fails closed before create/model acquisition and is
    /// never silently downgraded to V1.</summary>
    [Fact]
    public Task Unknown_profile_fails_closed_before_create()
        => StrataSelect((h, model) => new ProfileControlledSources(h.Admission, model,
            new StrataNativeProfile("mystery", 9, "whatever"), format: "Safetensors"),
            (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is InferenceEngineException && c.Message.Contains("unknown or unsupported", StringComparison.Ordinal));
            Assert.Equal(0, sources.Binary.WorkerReads);
            Assert.Equal(0, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });

    /// <summary>End-to-end factory V2 selection against the ACTUAL independently compiled native V2
    /// binary (6744db6d…): the truthful non-CUDA refusal ("No actual CUDA build exists") must surface
    /// from the factory path and the model lease must be retired exactly once. Requires the same
    /// environment variables the managed acceptance target uses; NOTRUN until Root's SDK slot provides
    /// them and the compiled binary.</summary>
    [Fact]
    public async Task Verified_factory_selection_reaches_truthful_non_CUDA_refusal_with_actual_binary()
    {
        var binary = Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_BINARY");
        var digest = Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_SHA256");
        Assert.False(string.IsNullOrWhiteSpace(binary));
        Assert.False(string.IsNullOrWhiteSpace(digest));
        Assert.True(File.Exists(binary));
        Assert.Equal(digest!.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(binary!))));
        await StrataSelect((h, model) =>
        {
            var sources = new ProfileControlledSources(h.Admission, model,
                new StrataNativeProfile("candidate", 2, StrataCandidateWorker.VerifiedCandidateSourceCommit), format: "GGUF");
            sources.Binary.ExecutablePath = binary!;
            sources.Binary.ExpectedSha256 = digest!;
            sources.Binary.PythonPath = "/usr/bin/python3";
            return sources;
        }, (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is NotSupportedException && c.Message.Contains("No actual CUDA build exists", StringComparison.Ordinal));
            Assert.Equal(1, sources.Binary.WorkerReads);
            Assert.Equal(1, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });
    }

    /// <summary>Defect-2 wiring probe against the ACTUAL compiled native V2 binary: before native
    /// start the candidate worker consults the held GGUF model fd-path lease (the SAME protected
    /// descriptor) and feeds it to the engine argv boundary. The genuine CUDA-false binary still
    /// surfaces the truthful non-CUDA refusal — no invented Ready/CUDA. Requires the same environment
    /// variables / compiled binary as the managed acceptance target; NOTRUN until Root's SDK slot
    /// provides them.</summary>
    [Fact]
    public async Task Verified_candidate_factory_consults_the_held_GGUF_model_fd_path_before_native_start()
    {
        var binary = Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_BINARY");
        var digest = Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_SHA256");
        Assert.False(string.IsNullOrWhiteSpace(binary));
        Assert.False(string.IsNullOrWhiteSpace(digest));
        Assert.True(File.Exists(binary));
        Assert.Equal(digest!.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(binary!))));
        await StrataSelect((h, model) =>
        {
            var sources = new ProfileControlledSources(h.Admission, model,
                new StrataNativeProfile("candidate", 2, StrataCandidateWorker.VerifiedCandidateSourceCommit), format: "GGUF");
            sources.Binary.ExecutablePath = binary!;
            sources.Binary.ExpectedSha256 = digest!;
            sources.Binary.PythonPath = "/usr/bin/python3";
            // Synthetic held fd path for this seam; the real host emits /proc/<pid>/fd/N.
            sources.Model.FdPath = "/proc/123/fd/9";
            return sources;
        }, (h, error, sources, causes) =>
        {
            Assert.Contains(causes, c => c is NotSupportedException && c.Message.Contains("No actual CUDA build exists", StringComparison.Ordinal));
            // The worker consulted the held GGUF model fd path (defect-2 wiring) before native start.
            Assert.Equal(1, sources.Model.FdPathReads);
            Assert.Equal(1, sources.Binary.WorkerReads);
            Assert.Equal(1, sources.Model.Closes);
            Assert.Equal(1, sources.Binary.Closes);
        });
    }

    private static async Task StrataSelect(Func<Harness, ModelIdentity, ProfileControlledSources> makeSources,
        Action<Harness, Exception?, ProfileControlledSources, Exception[]> assert)
    {
        await Control(async h =>
        {
            var model = new ModelIdentity(h.Provider.Id, "model");
            var sources = makeSources(h, model);
            var scope = new ProfileControlScope();
            var factory = new StrataManagedInferenceEngineFactory(h.Provider.Id, h.Adapter.OriginalConfiguredTarget,
                h.Admission, sources, sources, new SyntheticConfigurations(h.Provider), h.Coordinator, h.ObservedFrames,
                h.Tools, h.Context, h.Context);
            var error = await Record.ExceptionAsync(() => factory.CreateOriginalAsync(model, scope, CancellationToken.None));
            Assert.NotNull(error);
            var causes = Leaves(error!).ToArray();
            assert(h, error, sources, causes);
            foreach (var cause in causes) h.Expect(cause);
        });
    }

    private sealed class ProfileControlledSources : IOriginalStrataWorkerSource, IOriginalStrataModelSource
    {
        public readonly TaskRunAttemptAdmission Admission;
        private readonly ModelIdentity _model;
        public readonly ProfileControlledBinaryLease Binary;
        public readonly ProfileControlledModelLease Model;
        public ProfileControlledSources(TaskRunAttemptAdmission admission, ModelIdentity model,
            StrataNativeProfile? profile, string format)
        { Admission = admission; _model = model; Binary = new(profile); Model = new(format, model); }
        public Task<StrataOriginalWorkerLease> AcquireOriginalAsync(TaskRunAttemptAdmission admission,
            IInferenceEngineOriginalSourceScope scope, CancellationToken token)
        { Assert.Same(admission, Admission); return Task.FromResult<StrataOriginalWorkerLease>(Binary); }
        public Task<StrataOriginalModelLease> AcquireOriginalAsync(ModelIdentity sameModel, TaskRunAttemptAdmission admission,
            IInferenceEngineOriginalSourceScope scope, CancellationToken token)
        { Assert.Same(sameModel, _model); Assert.Same(admission, Admission); return Task.FromResult<StrataOriginalModelLease>(Model); }
        public bool IsIssuedOriginalWorkerLease(StrataOriginalWorkerLease lease, TaskRunAttemptAdmission admission)
            => ReferenceEquals(lease, Binary) && ReferenceEquals(admission, Admission);
        public bool IsIssuedOriginalModelLease(StrataOriginalModelLease lease, ModelIdentity sameModel, TaskRunAttemptAdmission admission)
            => ReferenceEquals(lease, Model) && ReferenceEquals(sameModel, _model) && ReferenceEquals(admission, Admission);
    }

    private sealed class ProfileControlledBinaryLease(StrataNativeProfile? profile) : StrataOriginalWorkerLease
    {
        public int CurrentChecks, WorkerReads, Closes;
        public string ExecutablePath = NonexistentExecutable;
        public string ExpectedSha256 = ZeroDigest64;
        public string? PythonPath = "/usr/bin/python3";
        public override StrataNativeProfile? InstalledProfile => profile;
        public override StrataBundledWorker OriginalWorker { get { WorkerReads++; return new(ExecutablePath, ExpectedSha256, PythonPath); } }
        public override void DemandCurrentOriginalBinding() { CurrentChecks++; }
        public override ValueTask DisposeAsync() { Closes++; return ValueTask.CompletedTask; }
    }

    private sealed class ProfileControlledModelLease(string format, ModelIdentity model) : StrataOriginalModelLease, IStrataOriginalModelSizeBinding
    {
        public int RequirementReads, CurrentChecks, Closes, FdPathReads;
        public string? FdPath;
        public override InferenceModelRequirements Requirements
        {
            get
            {
                RequirementReads++;
                return new(Model: model, ArtifactFingerprint: ZeroDigest64,
                    Architecture: "qwen3_5_moe", Family: "qwen35moe", WeightFormat: format, Quantization: "Q4_K_M",
                    RequiredFeatures: new HashSet<string> { "streaming" }, MinimumRamBytes: 0, MinimumVramBytes: 0,
                    ContextTokens: 128, NativeRegistration: "qwen35moe", Topology: "centralized");
            }
        }
        public override string OriginalCheckpointDirectory => Path.GetTempPath();
        public override string? OriginalCheckpointFileFdPath { get { FdPathReads++; return FdPath; } }
        public override IReadOnlyList<int> ActualCudaDeviceIndices => [];
        public long OriginalModelSizeBytes => 64L * 1024 * 1024;
        public override void DemandCurrentOriginalBinding() { CurrentChecks++; }
        public override ValueTask DisposeAsync() { Closes++; return ValueTask.CompletedTask; }
    }

    private sealed class ProfileControlScope : IInferenceEngineOriginalSourceScope
    {
        private readonly List<Task> _retained = [];
        public int FactoryInvocations, CleanupInvocations;
        public T InvokeOriginalFactory<T>(Func<T> actualFactory) { FactoryInvocations++; return actualFactory(); }
        public T InvokeOriginalCleanup<T>(Func<T> actualCleanup) { CleanupInvocations++; return actualCleanup(); }
        public void RetainOriginalTask(Task sameActualTask) { lock (_retained) _retained.Add(sameActualTask); }
    }
}
