using System.Collections.Frozen;

namespace Dulche.Runtime;

/// <summary>Runtime preference only. It never grants a model, context, provider or tool permission.</summary>
public enum InferenceEngine { Automatic = 0, LlamaCpp = 1, Strata = 2 }

/// <summary>Actual native error projection; the original exception remains retained by its source owner.</summary>
public sealed class InferenceEngineException(DulcheError error, Exception? originalCause=null) : Exception(error.Message,originalCause)
{ public DulcheError Error { get; } = error; }

/// <summary>Allows the documented Dulche.setInferenceEngine(Llama.cpp) spelling in C#.</summary>
public static class Llama { public const InferenceEngine cpp = InferenceEngine.LlamaCpp; }
/// <summary>Use static import for the documented Strata and Automatic selector spellings.</summary>
public static class InferenceEngines
{ public const InferenceEngine Strata = InferenceEngine.Strata; public const InferenceEngine Automatic = InferenceEngine.Automatic; }

public sealed record InferenceGpu(string Identity, int ComputeMajor, int ComputeMinor, long AvailableVramBytes);
public sealed record InferenceHardware(string Fingerprint, string OperatingSystem, string CpuArchitecture,
    long? AvailableRamBytes, IReadOnlyList<InferenceGpu> Gpus, IReadOnlySet<string> CpuFeatures);

/// <summary>The SAME canonical artifact, including all hard backend conditions. Dense/MoE is not a selector.</summary>
public sealed record InferenceModelRequirements(ModelIdentity Model, string ArtifactFingerprint,
    string Architecture, string Family, string WeightFormat, string Quantization,
    IReadOnlySet<string> RequiredFeatures, long MinimumRamBytes, long MinimumVramBytes,
    int ContextTokens, IReadOnlyList<InferenceEngine>? PreferredEngines = null,
    string? NativeRegistration = null, string Topology = "centralized");

public sealed record InferenceEngineSupport(InferenceEngine Engine, string RuntimeBuild,
    bool Available, string? UnavailableReason, IReadOnlySet<string> Architectures,
    IReadOnlySet<string> Families, IReadOnlySet<string> WeightFormats, IReadOnlySet<string> Quantizations,
    IReadOnlySet<string> Features, IReadOnlySet<string> OperatingSystems,
    IReadOnlySet<string> CpuArchitectures, IReadOnlySet<string> RequiredCpuFeatures,
    int MinimumCudaComputeMajor = 0, int MinimumCudaComputeMinor = 0,
    bool RequiresCuda = false, bool NcclBuilt = false, int? NumaNodes = null,
    IReadOnlySet<string>? NativeRegistrations = null);

public sealed record InferenceUnmetRequirement(string Requirement, string Expected, string Actual);
public sealed record InferenceCompatibility(InferenceEngine Engine, string RuntimeBuild,
    IReadOnlyList<InferenceUnmetRequirement> Unmet)
{ public bool Compatible => Unmet.Count == 0; }

public sealed record InferenceEngineDiagnostic(InferenceEngine Requested, InferenceEngine? Effective,
    string Reason, ModelIdentity? Model, string? HardwareFingerprint,
    IReadOnlyList<InferenceCompatibility> Compatibility, IReadOnlyList<DulcheError> InitializationFailures)
{
    /// <summary>Actual selected session mode; null during initialization or without an active engine.</summary>
    public InferenceEngine? ActiveSelectionPreference { get; init; }
    /// <summary>Requested override, including a pending preference that has not replaced the loaded session.</summary>
    public InferenceEngine PendingPreference { get; init; }
    public bool SelectionPending { get; init; }
    public string SelectionMode=>ActiveSelectionPreference is null ? "Unselected"
        :ActiveSelectionPreference==InferenceEngine.Automatic ? "Automatic" :"Manual";
}

/// <summary>Observed profile is metadata, not authority. Actual adapters must still validate/load their artifact.
/// Prefill/decode may be fused; KV export, image input and cancellation granularity are explicit features.</summary>
public sealed record InferenceRuntimeObservation(InferenceModelRequirements Requirements,
    InferenceHardware Hardware, IReadOnlyList<InferenceEngineSupport> Engines);
public interface IInferenceRuntimeObservationSource
{
    Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity sameModel,
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken);
}

/// <summary>Finite lifecycle scope, never a model/context/permission grant. A producer must carry it
/// through every original factory after awaits, capture Task/once-converted ValueTask inside the
/// callback and retain it before scope exit. Cleanup uses the explicit cleanup path after seal.</summary>
public interface IInferenceEngineOriginalSourceScope
{
    T InvokeOriginalFactory<T>(Func<T> actualFactory);
    T InvokeOriginalCleanup<T>(Func<T> actualCleanup);
    void RetainOriginalTask(Task sameActualTask);
}

/// <summary>Common execution contract reuses all existing load/generate/context/cancel/stream/error/shutdown APIs.
/// The adapter is the existing original managed authority owner, not a new TaskRun executor.</summary>
public abstract class OriginalInferenceEngineLease
{
    public abstract IDulcheOriginalProviderAdapter Adapter { get; }
    public abstract void DemandExternalOriginalJoin();
    public abstract Task CloseOriginalAsync();
}
public interface IInferenceEngineAdapterFactory
{
    /// <summary>A new actual session lease for each initialization. The owning factory preserves configured
    /// provider/context authority and owns any late successful native product after cancellation/refusal.
    /// Its failed encompassing Task must finish all acquired resource cleanup before exposing a typed
    /// recoverable initialization failure. Unknown cleanup failures remain full exception envelopes.</summary>
    Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity sameModel,
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken);
}
public sealed record InferenceEngineRegistration(InferenceEngine Engine, IInferenceEngineAdapterFactory Factory);

/// <summary>Observation of a genuinely initialized original raw engine, not a loading permission.
/// Generic providers retain their existing unsupported model-load behavior.</summary>
public interface IOriginalInferenceEngineModelSource
{
    Task<OperationResult<Unit>> ObserveOriginalInitializedModelAsync(ModelIdentity sameModel, CancellationToken cancellationToken);
}

public static class InferenceCompatibilityRegistry
{
    public static InferenceCompatibility Inspect(InferenceModelRequirements model, InferenceHardware hardware,
        InferenceEngineSupport engine)
    {
        var gaps = new List<InferenceUnmetRequirement>();
        void Require(bool condition, string name, object expected, object? actual)
        { if (!condition) gaps.Add(new(name, expected.ToString()!, actual?.ToString() ?? "Unknown")); }
        void Member(IReadOnlySet<string> values, string value, string name)
            => Require(values.Contains(value), name, value, string.Join(",", values.Order(StringComparer.Ordinal)));
        Require(engine.Engine is InferenceEngine.LlamaCpp or InferenceEngine.Strata, "engine.id", "Concrete engine", engine.Engine);
        Require(engine.Available && !string.IsNullOrWhiteSpace(engine.RuntimeBuild), "engine.available", "Actual available build", engine.UnavailableReason);
        Require(!string.IsNullOrWhiteSpace(model.ArtifactFingerprint), "model.artifact", "Bound original artifact fingerprint", model.ArtifactFingerprint);
        Require(!string.IsNullOrWhiteSpace(hardware.Fingerprint), "host.fingerprint", "Actual hardware observation fingerprint", hardware.Fingerprint);
        Member(engine.Architectures, model.Architecture, "model.architecture");
        Member(engine.Families, model.Family, "model.family");
        Member(engine.WeightFormats, model.WeightFormat, "model.weight-format");
        Member(engine.Quantizations, model.Quantization, "model.quantization");
        Member(engine.OperatingSystems, hardware.OperatingSystem, "host.os");
        Member(engine.CpuArchitectures, hardware.CpuArchitecture, "host.cpu-architecture");
        foreach (var feature in model.RequiredFeatures.Order(StringComparer.Ordinal)) Member(engine.Features, feature, "runtime.feature");
        foreach (var feature in engine.RequiredCpuFeatures.Order(StringComparer.Ordinal)) Member(hardware.CpuFeatures, feature, "host.cpu-feature");
        Require(model.MinimumRamBytes >= 0 && hardware.AvailableRamBytes >= model.MinimumRamBytes,
            "host.ram", model.MinimumRamBytes, hardware.AvailableRamBytes);
        Require(model.ContextTokens > 0, "runtime.context", "Positive context", model.ContextTokens);
        Require(model.MinimumVramBytes >= 0 && hardware.Gpus.All(gpu => gpu.AvailableVramBytes >= 0),
            "host.vram-observation", "Nonnegative actual memory", model.MinimumVramBytes);
        Require(hardware.Gpus.Select(gpu => gpu.Identity).Distinct(StringComparer.Ordinal).Count() == hardware.Gpus.Count
            && hardware.Gpus.All(gpu => !string.IsNullOrWhiteSpace(gpu.Identity)), "host.gpu-identity", "Distinct physical devices", hardware.Gpus.Count);
        var vram = hardware.Gpus.Aggregate(0L, (total, gpu) => gpu.AvailableVramBytes > 0 && total > long.MaxValue - gpu.AvailableVramBytes
            ? long.MaxValue : total + Math.Max(0,gpu.AvailableVramBytes));
        Require(vram >= model.MinimumVramBytes, "host.vram", model.MinimumVramBytes, vram);
        if (engine.RequiresCuda)
        {
            Require(hardware.Gpus.Count > 0, "host.cuda", "Observed CUDA device", hardware.Gpus.Count);
            foreach (var gpu in hardware.Gpus)
                Require(gpu.ComputeMajor > engine.MinimumCudaComputeMajor || gpu.ComputeMajor == engine.MinimumCudaComputeMajor
                    && gpu.ComputeMinor >= engine.MinimumCudaComputeMinor, "host.cuda-compute:" + gpu.Identity,
                    $"{engine.MinimumCudaComputeMajor}.{engine.MinimumCudaComputeMinor}", $"{gpu.ComputeMajor}.{gpu.ComputeMinor}");
        }
        if (engine.Engine == InferenceEngine.Strata)
        {
            Require(model.WeightFormat == "Safetensors", "strata.weight-format", "Original Safetensors", model.WeightFormat);
            Require(model.NativeRegistration is not null && engine.NativeRegistrations?.Contains(model.NativeRegistration) == true,
                "strata.registration", "Actual native registry entry", model.NativeRegistration);
            Require(model.Topology is "centralized" or "rank-local-tp2", "strata.topology", "centralized or rank-local-tp2", model.Topology);
            if (model.Topology == "rank-local-tp2")
            {
                Require(hardware.Gpus.Count == 2, "strata.tp2.devices", 2, hardware.Gpus.Count);
                Require(hardware.Gpus.All(gpu => gpu.ComputeMajor == 8 && gpu.ComputeMinor == 6), "strata.tp2.compute", "8.6", string.Join(",", hardware.Gpus.Select(gpu => $"{gpu.ComputeMajor}.{gpu.ComputeMinor}")));
                Require(engine.NcclBuilt, "strata.tp2.nccl", "NCCL build", engine.NcclBuilt);
                Require(engine.NumaNodes >= 2, "strata.tp2.numa", "At least 2", engine.NumaNodes);
                Require(model.ContextTokens <= 65536, "strata.tp2.context", "At most 65536", model.ContextTokens);
            }
        }
        return new(engine.Engine, engine.RuntimeBuild, gaps.AsReadOnly());
    }

    internal static InferenceRuntimeObservation Detach(InferenceRuntimeObservation original)
    {
        var model = original.Requirements with { RequiredFeatures = original.Requirements.RequiredFeatures.ToFrozenSet(StringComparer.Ordinal),
            PreferredEngines = original.Requirements.PreferredEngines?.ToArray() };
        var hardware = original.Hardware with { Gpus = original.Hardware.Gpus.ToArray(), CpuFeatures = original.Hardware.CpuFeatures.ToFrozenSet(StringComparer.Ordinal) };
        var engines = original.Engines.Select(value => value with {
            Architectures = value.Architectures.ToFrozenSet(StringComparer.Ordinal), Families = value.Families.ToFrozenSet(StringComparer.Ordinal),
            WeightFormats = value.WeightFormats.ToFrozenSet(StringComparer.Ordinal), Quantizations = value.Quantizations.ToFrozenSet(StringComparer.Ordinal),
            Features = value.Features.ToFrozenSet(StringComparer.Ordinal), OperatingSystems = value.OperatingSystems.ToFrozenSet(StringComparer.Ordinal),
            CpuArchitectures = value.CpuArchitectures.ToFrozenSet(StringComparer.Ordinal), RequiredCpuFeatures = value.RequiredCpuFeatures.ToFrozenSet(StringComparer.Ordinal),
            NativeRegistrations = value.NativeRegistrations?.ToFrozenSet(StringComparer.Ordinal) }).ToArray();
        return new(model, hardware, engines);
    }
}
