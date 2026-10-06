using Dulche.Runtime;
using Haven.Application;
namespace Haven.Infrastructure;

/// <summary>Detached integrity observations supplied by the genuine configured installation
/// owner. Constructing this record, naming a path or matching its digest issues no authority.</summary>
public sealed record StrataInstalledFile(string RelativeName, long Length, string Sha256);

/// <summary>The actual installation owner holds its original signed/approved installation
/// and model read authorization. No implementation is inferred from a hash, settings, Home
/// routing preferences, a Task actor or a Codex connector. Missing producer is setup-required.</summary>
public abstract class StrataVerifiedInstallationLease : IAsyncDisposable
{
    public abstract TaskRunAttemptAdmission OriginalAdmission { get; }
    public abstract ModelIdentity OriginalModel { get; }
    public abstract string OriginalWorkerRoot { get; }
    public abstract StrataInstalledFile OriginalWorkerFile { get; }
    public abstract string OriginalCheckpointRoot { get; }
    public abstract IReadOnlyList<StrataInstalledFile> OriginalCheckpointFiles { get; }
    public abstract InferenceModelRequirements OriginalRequirements { get; }
    /// <summary>Actual verified package/build capability inventory, never copied from model
    /// requirements or user settings. Null means that native capability observation is absent.</summary>
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
