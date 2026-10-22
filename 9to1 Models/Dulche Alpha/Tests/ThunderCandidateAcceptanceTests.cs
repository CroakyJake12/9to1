using System.Security.Cryptography;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>
/// Actual production managed registry + actual independently compiled native V2 binary.
/// The supplied model lease is a test contract only. No weights, engine, fake hello, GPU readiness,
/// tool execution or successful model load is manufactured. GGUF admission is an expected gap.
/// </summary>
public sealed class ThunderCandidateAcceptanceTests
{
    [Theory]
    [InlineData("qwen35moe")]
    [InlineData("ornith-ai/Ornith-1.5-122B")]
    [InlineData("InternScience/Sage-A1.5")]
    public void Confirmed_registrations_resolve_in_actual_managed_registry(string registration)
    {
        Assert.True(StrataCapabilityRegistry.Resolve(registration,out var family));
        Assert.Equal("qwen35moe",family);
    }

    [Theory]
    [InlineData("orinth-1.5")]
    [InlineData("unknown-model")]
    public void Unverified_registrations_are_not_silently_relabelled(string registration)
    {
        Assert.False(StrataCapabilityRegistry.Resolve(registration,out var family));
        Assert.Null(family);
    }

    [Fact]
    public async Task Actual_V2_binary_handshake_reaches_truthful_non_CUDA_refusal_and_retires()
    {
        var result=await AcquireRefusal("GGUF");
        Assert.Contains(Causes(result.Error),cause=>cause is NotSupportedException
            && cause.Message.Contains("No actual CUDA build exists",StringComparison.Ordinal));
        Assert.Equal(1,result.Binding.Closed);
    }

    [Fact]
    public async Task GGUF_registration_must_reach_candidate_handshake_instead_of_legacy_format_refusal()
    {
        // Intentionally exposes the source12 compatibility gap. A real GGUF file/load is NOT
        // claimed; correct format admission should reach this genuine binary's non-CUDA refusal.
        var result=await AcquireRefusal("GGUF");
        Assert.DoesNotContain(Causes(result.Error),cause=>cause.Message.Contains("Safetensors registration",StringComparison.Ordinal));
        Assert.Contains(Causes(result.Error),cause=>cause is NotSupportedException
            && cause.Message.Contains("No actual CUDA build exists",StringComparison.Ordinal));
        Assert.Equal(1,result.Binding.Closed);
    }

    private static async Task<(Exception Error,Binding Binding)> AcquireRefusal(string format)
    {
        Assert.True(OperatingSystem.IsLinux(),"This actual binary acceptance target is Linux x64 only.");
        var binary=Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_BINARY");
        var digest=Environment.GetEnvironmentVariable("DULCHE_THUNDER_ACCEPTANCE_SHA256");
        Assert.False(string.IsNullOrWhiteSpace(binary));
        Assert.False(string.IsNullOrWhiteSpace(digest));
        Assert.True(File.Exists(binary));
        Assert.Equal(digest!.ToUpperInvariant(),Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(binary!))));
        var binding=new Binding(format);
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var acquisition=StrataCandidateWorker.CreateCandidateAfterPublicationAsync(Task.CompletedTask,
            new StrataBundledWorker(binary!,digest!,"/usr/bin/python3"),binding,cancellation.Token);
        StrataCandidateWorker? unexpected=null;
        var error=await Record.ExceptionAsync(async()=> { unexpected=await acquisition.WaitAsync(TimeSpan.FromSeconds(8)); });
        if(unexpected is not null) await unexpected.DisposeAsync();
        Assert.NotNull(error);
        Assert.True(acquisition.IsCompleted,"Original acquisition must settle before the test ends.");
        return (error!,binding);
    }

    private sealed class Binding(string format):StrataOriginalModelLease
    {
        public int Closed { get; private set; }
        public override InferenceModelRequirements Requirements { get; }=new(
            new ModelIdentity("acceptance","same-test-model","same-test-artifact"),
            "same-test-artifact","qwen3_5_moe","qwen35moe",format,"Q4_K_M",
            new HashSet<string>{"streaming"},0,0,128,NativeRegistration:"qwen35moe");
        public override string OriginalCheckpointDirectory=>Path.GetTempPath();
        public override IReadOnlyList<int> ActualCudaDeviceIndices=>[];
        public override void DemandCurrentOriginalBinding()
        { if(Closed!=0) throw new InvalidOperationException("Test contract already retired."); }
        public override ValueTask DisposeAsync() { Closed++;return ValueTask.CompletedTask; }
    }

    private static IEnumerable<Exception> Causes(Exception original)
    {
        yield return original;
        if(original is AggregateException aggregate)
            foreach(var inner in aggregate.InnerExceptions)
                foreach(var cause in Causes(inner)) yield return cause;
        else if(original.InnerException is { } inner)
            foreach(var cause in Causes(inner)) yield return cause;
    }
}
