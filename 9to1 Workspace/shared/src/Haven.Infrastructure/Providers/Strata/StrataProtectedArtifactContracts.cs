using Dulche.Runtime;
using Haven.Application;
namespace Haven.Infrastructure;

/// <summary>Detached integrity observations supplied by the genuine configured installation
/// owner. Constructing this record, naming a path or matching its digest issues no authority.</summary>
public sealed record StrataInstalledFile(string RelativeName, long Length, string Sha256);

/// <summary>The actual installation owner holds its original signed/approved installation
/// and model read authorization. No implementation is inferred from a hash, settings, Home
/// routing preferences, a Task actor or a Codex connector. A configured developer-artifact
/// variant may instead hold the SAME explicit individual Home read/use approval over the full
/// tuple; that does not assert publisher trust, signed system installation, or Ready status.
/// Its current Home claim and original Task/model authority remain independently mandatory.
/// Missing producer or missing required artifact/hardware evidence is setup-required.</summary>
public abstract class StrataVerifiedInstallationLease : IAsyncDisposable
{
    public abstract TaskRunAttemptAdmission OriginalAdmission { get; }
    public abstract ModelIdentity OriginalModel { get; }
    public abstract string OriginalWorkerRoot { get; }
    public abstract StrataInstalledFile OriginalWorkerFile { get; }
    public abstract string OriginalCheckpointRoot { get; }
    public abstract IReadOnlyList<StrataInstalledFile> OriginalCheckpointFiles { get; }
    public abstract InferenceModelRequirements OriginalRequirements { get; }
    /// <summary>Trusted installed native profile identity (null => legacy V1/ABI 1 default). Supplied
    /// only by the genuine installation owner for a verified candidate installation; a bare path/hash/
    /// profile label alone grants nothing.</summary>
    public virtual StrataNativeProfile? InstalledProfile => null;
    /// <summary>Approved absolute interpreter path for the bundled tokenizer helper (candidate V2).
    /// Null means no approved interpreter is supplied; the candidate then refuses text generation with a
    /// precise missing-dependency cause. Never inherited from ambient PATH or secrets.</summary>
    public virtual string? OriginalPythonInterpreterPath => null;
    /// <summary>Candidate V2 bundle-closure root: the sibling engine directory (e.g.
    /// runtimes/&lt;target&gt;/engine). Null for legacy V1 single-executable semantics. Supplied only by
    /// the genuine installation owner for a verified candidate installation; a path/hash alone grants
    /// nothing, and the whole engine/helper closure must be actually held and digest-pinned before use.</summary>
    public virtual string? OriginalEngineRoot => null;
    /// <summary>Candidate V2 engine sibling observation (base name "strata" under OriginalEngineRoot);
    /// null for legacy V1.</summary>
    public virtual StrataInstalledFile? OriginalEngineFile => null;
    /// <summary>Candidate V2 helper text-path closure observations (f our pinned members under
    /// OriginalWorkerRoot); null for legacy V1.</summary>
    public virtual IReadOnlyList<StrataInstalledFile>? OriginalHelperFiles => null;
    /// <summary>Candidate V2 optional native library closure observations (e.g. the K-quant MMQ
    /// DSO libstrata_kq.so beside the engine under OriginalEngineRoot); null for legacy V1. Supplied
    /// only by the genuine installation owner for a verified candidate installation; a path/hash
    /// alone grants nothing, and the whole closure must be actually held and digest-pinned before use.</summary>
    public virtual IReadOnlyList<StrataInstalledFile>? OriginalNativeLibraries => null;
    /// <summary>Actual verified package/build capability inventory, never copied from model
    /// requirements or user settings. An explicitly Home-reviewed independent developer-signed
    /// inventory is verified against the exact worker; actual native hello/hardware must still
    /// agree. Its developer key creates no system publisher trust. Null means that native
    /// capability observation is absent.</summary>
    public virtual InferenceEngineSupport? OriginalBuildSupport => null;
    public abstract IReadOnlyList<int> OriginalCudaDeviceIndices { get; }
    public abstract void DemandCurrentOriginalInstallation();
    public abstract ValueTask DisposeAsync();
}
public interface IStrataVerifiedInstallationSource
{
    Task<StrataVerifiedInstallationLease> AcquireOriginalAsync(ModelIdentity sameModel,
        TaskRunAttemptAdmission sameAdmission, IInferenceEngineOriginalSourceScope originalScope, CancellationToken token);
    bool IsIssuedOriginalInstallation(StrataVerifiedInstallationLease sameLease,
        ModelIdentity sameModel, TaskRunAttemptAdmission sameAdmission);
}

/// <summary>A live file binding, not initialized/resident native model proof. The installation
/// source's revision is compared exactly; this producer never derives ArtifactRevision from SHA.</summary>
public interface IStrataOriginalArtifactBinding
{
    ModelIdentity OriginalModel { get; }
    TaskRunAttemptAdmission OriginalAdmission { get; }
    StrataOriginalHardwareObservation OriginalHardwareProbe { get; }
    InferenceEngineSupport? OriginalBuildSupport { get; }
    void DemandCurrentOriginalBinding();
}

/// <summary>Binds a detached observer to the SAME genuine issued request. Construction performs
/// no observation or authorization; the eventual observation still requires all private sources.</summary>
public interface IStrataOriginalRequestObservationSource
{
    IInferenceRuntimeObservationSource BindOriginalRequest(TaskRunAttemptAdmission sameAdmission);
}
