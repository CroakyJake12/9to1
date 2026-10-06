using System.Reflection;
using Dulche.Runtime;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Real cache controls with explicitly synthetic timing issuers. Reflection constructs
/// test data only; these controls do not observe CUDA, an installed model or native performance.</summary>
public sealed class InferenceCalibrationTests
{
    [Fact]
    public async Task A_copied_reading_cannot_enter_the_cache_and_one_missing_candidate_stays_uncalibrated()
    {
        await using var cache=new InferenceCalibrationCache(); var fresh=Observation();
        var llama=new Source(Reading(InferenceEngine.LlamaCpp,1,1));
        var strata=new Source(Reading(InferenceEngine.Strata,0.1,0.1));
        var copied=Make(llama.Value.Key,llama.Value.Actual);
        Assert.False(cache.RecordOriginal(llama,copied));
        Assert.True(cache.RecordOriginal(llama,llama.Value));
        Assert.False(cache.RecordOriginal(llama,llama.Value));
        Assert.False(cache.TryOrder(fresh,[InferenceEngine.LlamaCpp,InferenceEngine.Strata],out _,out var missing));
        Assert.Contains("Uncalibrated",missing);
        Assert.True(cache.RecordOriginal(strata,strata.Value));
        Assert.True(cache.TryOrder(fresh,[InferenceEngine.LlamaCpp,InferenceEngine.Strata],out var actualOrder,out var measured));
        Assert.Equal(new[]{InferenceEngine.Strata,InferenceEngine.LlamaCpp},actualOrder);
        Assert.Contains("All compatible candidates",measured);
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("hardware")]
    [InlineData("runtime")]
    [InlineData("context")]
    public async Task Cached_measurements_cannot_rank_a_different_current_binding(string changed)
    {
        await using var cache=new InferenceCalibrationCache();
        var source=new Source(Reading(InferenceEngine.Strata,0.1,0.1));
        Assert.True(cache.RecordOriginal(source,source.Value));
        var fresh=Observation();
        fresh=changed switch {
            "artifact"=>fresh with { Requirements=fresh.Requirements with { ArtifactFingerprint="different" } },
            "hardware"=>fresh with { Hardware=fresh.Hardware with { Fingerprint="different" } },
            "runtime"=>fresh with { Engines=fresh.Engines.Select(row=>row with { RuntimeBuild="different" }).ToArray() },
            _=>fresh with { Requirements=fresh.Requirements with { ContextTokens=4096 } }
        };
        Assert.False(cache.TryOrder(fresh,[InferenceEngine.Strata],out var unchanged,out var reason));
        Assert.Equal(new[]{InferenceEngine.Strata},unchanged); Assert.Contains("Uncalibrated",reason);
    }

    [Fact]
    public async Task Nonfinite_telemetry_and_existing_external_diagnostic_JSON_are_never_measurement_authority()
    {
        var directory=Path.Combine(Path.GetTempPath(),"dulche-calibration-control-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path=Path.Combine(directory,"calibration.json");
        InferenceCalibrationCache? cache=null;
        try {
            await File.WriteAllTextAsync(path,"{\"schema\":1,\"samples\":[{\"measured\":true}]}",CancellationToken.None);
            Assert.Throws<NotSupportedException>(()=>new InferenceCalibrationCache(path));
            cache=new(); var source=new Source(Reading(InferenceEngine.Strata,double.NaN,0.1));
            Assert.False(cache.RecordOriginal(source,source.Value));
            Assert.False(cache.TryOrder(Observation(),[InferenceEngine.Strata],out _,out var reason));
            Assert.Contains("Uncalibrated",reason);
            Assert.Contains("measured",await File.ReadAllTextAsync(path,CancellationToken.None));
        }
        finally { if(cache is not null) await cache.DisposeAsync(); Directory.Delete(directory,true); }
    }

    [Fact]
    public void Path_only_persistence_cannot_create_a_missing_parent_or_publish_a_write_driver()
    {
        var directory=Path.Combine(Path.GetTempPath(),"dulche-denied-calibration-"+Guid.NewGuid().ToString("N"));
        var path=Path.Combine(directory,"calibration.json");
        Assert.False(Directory.Exists(directory));
        var cause=Assert.Throws<NotSupportedException>(()=>new InferenceCalibrationCache(path));
        Assert.Contains("verifier-issued writable owner",cause.Message);
        Assert.False(Directory.Exists(directory)); Assert.False(File.Exists(path));
    }

    private static InferenceCalibrationReading Reading(InferenceEngine engine,double prefill,double decode)
        =>Make(new(engine,new("fixture-provider","same-model","fixture-revision"),"artifact","hardware","runtime",2048,InferenceCalibrationCache.DefaultSettings),
            new(100,100,100,prefill,decode,null,false,false));
    private static InferenceCalibrationReading Make(InferenceCalibrationKey key,StrataGenerationObservation actual)
        =>(InferenceCalibrationReading)typeof(InferenceCalibrationReading).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke([key,actual]);
    private static InferenceRuntimeObservation Observation()
    {
        var requirements=new InferenceModelRequirements(new("fixture-provider","same-model","fixture-revision"),"artifact","fixture","fixture","fixture","fixture",new HashSet<string>(),1,1,2048);
        InferenceEngineSupport Support(InferenceEngine engine)=>new(engine,"runtime",true,null,new HashSet<string>(),new HashSet<string>(),new HashSet<string>(),new HashSet<string>(),new HashSet<string>(),new HashSet<string>(),new HashSet<string>(),new HashSet<string>());
        return new(requirements,new("hardware","Linux","x64",4096,[],new HashSet<string>()),[Support(InferenceEngine.LlamaCpp),Support(InferenceEngine.Strata)]);
    }
    private sealed class Source(InferenceCalibrationReading actual):IOriginalInferenceCalibrationSource
    {
        public InferenceCalibrationReading Value { get; }=actual;
        public bool IsIssuedOriginalCalibration(InferenceCalibrationReading sameOriginal)=>ReferenceEquals(Value,sameOriginal);
        public bool TryObserveOriginalCalibration(ModelIdentity sameModel,out InferenceCalibrationReading? reading)
        { reading=Value.Key.Model==sameModel ? Value : null; return reading is not null; }
    }
    private static async Task<Exception?> Capture(Task actual) { try { await actual; return null; } catch(Exception error) { return error; } }
    private static IEnumerable<Exception> Graph(Exception actual)
    { yield return actual; if(actual is AggregateException group) foreach(var child in group.InnerExceptions) foreach(var cause in Graph(child)) yield return cause; else if(actual.InnerException is { } inner) foreach(var cause in Graph(inner)) yield return cause; }
}
