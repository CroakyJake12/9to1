using System.Runtime.CompilerServices;

namespace Dulche.Runtime;

/// <summary>Bounded actual normal-request timings in memory. Path-only persistence is unavailable:
/// it requires a separately issued writable owner; external JSON never becomes measurement authority.</summary>
public sealed class InferenceCalibrationCache : IAsyncDisposable
{
    public const string DefaultSettings="medium;t=0.7;p=1;k=0;max=256;seed=33377335";
    private const int Capacity=256;
    private readonly object _gate=new();
    private readonly Dictionary<InferenceCalibrationKey,Sample> _samples=[];
    private readonly ConditionalWeakTable<InferenceCalibrationReading,object> _recorded=new();
    [ThreadStatic] private static List<InferenceCalibrationCache>? _physical;
    private bool _sealed;
    private Task? _close;
    public InferenceCalibrationCache(string? configuredLocalDiagnosticFile=null)
    {
        if(configuredLocalDiagnosticFile is not null)
            throw new NotSupportedException("Calibration persistence requires a genuine verifier-issued writable owner; a file path is insufficient.");
    }

    public bool RecordOriginal(IOriginalInferenceCalibrationSource sameSource, InferenceCalibrationReading sameReading)
    {
        if(!Physical(()=>sameSource.IsIssuedOriginalCalibration(sameReading))||!Measured(sameReading)) return false;
        lock(_gate) {
            if(_sealed||_recorded.TryGetValue(sameReading,out _)||(_samples.Count>=Capacity&&!_samples.ContainsKey(sameReading.Key))) return false;
            var actual=sameReading.Actual;
            var prefill=actual.PrefillSeconds/actual.PrefillTokens; var decode=actual.DecodeSeconds/actual.DecodeTokens;
            var next=_samples.TryGetValue(sameReading.Key,out var prior)
                ? new Sample(prior.PrefillPerToken*(7d/8)+prefill/8,prior.DecodePerToken*(7d/8)+decode/8,Math.Min(8,prior.Observations+1))
                : new Sample(prefill,decode,1);
            _samples[sameReading.Key]=next; _recorded.Add(sameReading,new object());
            return true;
        }
    }

    public bool TryOrder(InferenceRuntimeObservation fresh, IReadOnlyList<InferenceEngine> compatible,
        out InferenceEngine[] ordered, out string reason)
    {
        ordered=compatible.ToArray(); reason="Uncalibrated: comparable actual normal-request prefill/decode observations are unavailable.";
        if(compatible.Count==0) return false;
        var costs=new Dictionary<InferenceEngine,double>();
        lock(_gate) foreach(var engine in compatible) {
            var support=fresh.Engines.SingleOrDefault(row=>row.Engine==engine);
            if(support is null) return false;
            var key=new InferenceCalibrationKey(engine,fresh.Requirements.Model,fresh.Requirements.ArtifactFingerprint,
                fresh.Hardware.Fingerprint,support.RuntimeBuild,fresh.Requirements.ContextTokens,DefaultSettings);
            if(!_samples.TryGetValue(key,out var sample)) return false;
            costs[engine]=sample.PrefillPerToken*Math.Min(fresh.Requirements.ContextTokens,2048)+sample.DecodePerToken*256;
            if(!double.IsFinite(costs[engine])||costs[engine]<=0) return false;
        }
        ordered=compatible.OrderBy(engine=>costs[engine]).ThenBy(engine=>Array.IndexOf(compatible.ToArray(),engine)).ToArray();
        reason="Actual cached prefill/decode seconds per token for this model/artifact/hardware/runtime/context; estimated 2048-or-context prefill plus 256 decode tokens. All compatible candidates have comparable observations; no quality or precision settings change.";
        return true;
    }

    private static bool Measured(InferenceCalibrationReading reading)
    {
        var key=reading.Key; var value=reading.Actual;
        return key.Engine is InferenceEngine.Strata or InferenceEngine.LlamaCpp
            &&!string.IsNullOrWhiteSpace(key.ArtifactFingerprint)&&!string.IsNullOrWhiteSpace(key.HardwareFingerprint)
            &&!string.IsNullOrWhiteSpace(key.RuntimeFingerprint)&&key.ContextTokens>0&&key.Settings==DefaultSettings
            &&value.PrefillTokens>0&&value.DecodeTokens>0&&value.PromptTokens>=value.PrefillTokens
            &&double.IsFinite(value.PrefillSeconds)&&double.IsFinite(value.DecodeSeconds)&&value.PrefillSeconds>0&&value.DecodeSeconds>0
            &&double.IsFinite(value.PrefillSeconds/value.PrefillTokens)&&value.PrefillSeconds/value.PrefillTokens>0
            &&double.IsFinite(value.DecodeSeconds/value.DecodeTokens)&&value.DecodeSeconds/value.DecodeTokens>0
            &&(value.ReusedPromptTokens is null||value.ReusedPromptTokens>=0&&value.ReusedPromptTokens<=value.PromptTokens);
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalJoin();
        lock(_gate) { _sealed=true; _close??=Task.CompletedTask; return new(_close); }
    }
    public void DemandExternalOriginalJoin()
    {
        if(_physical?.Contains(this)==true) throw new InvalidOperationException("Physical calibration callback cannot close its own observation owner.");
    }
    private T Physical<T>(Func<T> callback)
    {
        var values=_physical??=[]; values.Add(this);
        try { return callback(); } catch(OperationCanceledException error) { throw new AggregateException("Calibration callback faulted synchronously.",error); }
        finally { values.RemoveAt(values.Count-1); }
    }
    private sealed record Sample(double PrefillPerToken,double DecodePerToken,int Observations);
}
