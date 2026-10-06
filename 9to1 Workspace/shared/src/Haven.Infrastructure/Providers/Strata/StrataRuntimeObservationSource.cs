using Dulche.Runtime;
using Haven.Application;
using System.Collections.Frozen;
namespace Haven.Infrastructure;

/// <summary>Observations for an already genuine per-request admission. This does not establish
/// resident model initialization or issue installation/context/tool authority.</summary>
public sealed class StrataRuntimeObservationSource(StrataNativeArtifactSource artifacts,
    Func<TaskRunAttemptAdmission?> originalAdmission) : IInferenceRuntimeObservationSource, IStrataOriginalRequestObservationSource
{
    /// <summary>The unbound global descriptor refuses taskless observation.</summary>
    public StrataRuntimeObservationSource(StrataNativeArtifactSource artifacts) : this(artifacts, () => null) { }

    public IInferenceRuntimeObservationSource BindOriginalRequest(TaskRunAttemptAdmission sameAdmission)
    {
        ArgumentNullException.ThrowIfNull(sameAdmission);
        // A new immutable closure per request; no global current-admission slot or I/O.
        return new StrataRuntimeObservationSource(artifacts, () => sameAdmission);
    }

    public async Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,
        IInferenceEngineOriginalSourceScope scope, CancellationToken token)
    {
        TaskRunAttemptAdmission? admission = null;
        Task<StrataOriginalModelLease>? raw = null; StrataOriginalModelLease? lease = null; Task? close = null;
        var errors = new List<Exception>(); InferenceRuntimeObservation? result = null;
        try
        {
            admission = Invoke(originalAdmission)
                ?? throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
                    "STRATA_ORIGINAL_REQUEST_MODEL_USE_ADMISSION_REQUIRED", model.StableKey, false));
            _ = Invoke(() => { raw = artifacts.AcquireOriginalAsync(model, admission, scope, token); scope.RetainOriginalTask(raw); return raw; });
            lease = await (raw ?? throw new InvalidOperationException("No original artifact acquisition Task was captured.")).ConfigureAwait(false);
            if (!artifacts.IsIssuedOriginalModelLease(lease, model, admission)) throw new UnauthorizedAccessException("SAME actual original model lease is required.");
            Invoke(() => { lease.DemandCurrentOriginalBinding(); return 0; });
            if (lease is not IStrataOriginalArtifactBinding binding) throw new InvalidDataException("The actual protected artifact observation port is missing.");
            var hardware = binding.OriginalHardwareProbe;
            var requirements = Invoke(() => ObserveRequestRequirements(lease.Requirements, admission));
            var support = ObserveBuildSupport(hardware, binding.OriginalBuildSupport);
            result = new(requirements, hardware.Hardware, new[] { support });
        }
        catch (Exception error) { Add(raw, error); }
        finally
        {
            // Retain and recover a successful late product even when the caller scope failed
            // after actual Task acquisition, then close it independently before disclosure.
            if (raw is not null) try { lease ??= await raw.ConfigureAwait(false); } catch (Exception error) { Add(raw, error); }
            if (lease is not null)
            {
                try { _ = Invoke(() => { close = lease.DisposeAsync().AsTask(); scope.RetainOriginalTask(close); return close; }, cleanup: true); }
                catch (Exception error) { Add(close, error); }
                if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { Add(close, error); }
            }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException(errors);
        return result ?? throw new InvalidDataException("No actual runtime observation exists.");
        void Add(Task? task, Exception error)
        { if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error); if (task?.Exception is { } group) {
                if (!errors.Any(item => ReferenceEquals(item, group))) errors.Add(group);
                foreach (var cause in group.InnerExceptions) if (!errors.Any(item => ReferenceEquals(item, cause))) errors.Add(cause); } }
        T Invoke<T>(Func<T> factory, bool cleanup = false)
        {
            var thread = Environment.CurrentManagedThreadId; var active = 1; var used = 0; T captured = default!; Exception? callbackFailure = null;
            try
            {
                T Once()
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                        throw new InvalidOperationException("Actual runtime observation callback is inactive, foreign-thread or consumed.");
                    try { return captured = factory(); }
                    catch (Exception error) { callbackFailure = error; throw; }
                }
                _ = cleanup ? scope.InvokeOriginalCleanup(Once) : scope.InvokeOriginalFactory(Once);
                if (callbackFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("Actual runtime observation callback was not invoked.");
                return captured;
            }
            catch (Exception error)
            {
                if (callbackFailure is not null && !ReferenceEquals(callbackFailure, error)) Add(null, callbackFailure);
                if (error is OperationCanceledException) throw new AggregateException("Actual synchronous runtime observation source fault.", error);
                throw;
            }
            finally { Interlocked.Exchange(ref active, 0); }
        }
    }
    private static InferenceModelRequirements ObserveRequestRequirements(InferenceModelRequirements original,
        TaskRunAttemptAdmission sameAdmission) => original with
    {
        // This exact privately admitted request can require more than its installation
        // manifest. It is a hard fit condition, never engine support or a permission grant.
        RequiredFeatures = original.RequiredFeatures.Concat(sameAdmission.Lease.Candidate.RequiredCapabilities)
            .ToFrozenSet(StringComparer.Ordinal)
    };
    private static InferenceEngineSupport ObserveBuildSupport(StrataOriginalHardwareObservation hardware, InferenceEngineSupport? verifiedBuild)
    {
        if (verifiedBuild is null || verifiedBuild.Engine != InferenceEngine.Strata || verifiedBuild.RuntimeBuild != hardware.RuntimeBuild ||
            !verifiedBuild.Available || !hardware.CudaBuilt || !verifiedBuild.RequiresCuda || verifiedBuild.NcclBuilt != hardware.NcclBuilt ||
            verifiedBuild.NativeRegistrations is null || verifiedBuild.NativeRegistrations.Any(value => !hardware.NativeRegistrations.Contains(value)))
            return new(InferenceEngine.Strata, hardware.RuntimeBuild, false, "STRATA_VERIFIED_NATIVE_CAPABILITY_INVENTORY_REQUIRED",
                FrozenSet<string>.Empty, FrozenSet<string>.Empty, FrozenSet<string>.Empty, FrozenSet<string>.Empty, FrozenSet<string>.Empty,
                FrozenSet<string>.Empty, FrozenSet<string>.Empty, FrozenSet<string>.Empty, RequiresCuda: true);
        // The real package inventory and observed hello are independent of requested requirements.
        // No architecture/family/quantization/required feature is copied from the model request.
        // Package inventory cannot advertise features that this bundled managed/native
        // request bridge does not implement. Intersect; never add a feature from demand.
        return verifiedBuild with { Features = verifiedBuild.Features
            .Where(value => value is "Text" or "Streaming").ToFrozenSet(StringComparer.Ordinal) };
    }
}
