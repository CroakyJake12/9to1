using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Actual decoder and Linux synthetic-wire custody controls; CUDA/registry hello is invented, no readiness proof.</summary>
public sealed class StrataNativeProtocolTests
{
    [Fact]
    public void Actual_decoder_retains_split_UTF8_scalars_until_complete_and_requires_terminal_flush()
    {
    var decoder=Decoder();
    Assert.Equal("",Feed(decoder,[0xf0,0x9f],false));
    Assert.Equal("",Feed(decoder,[0x98],false));
    Assert.Equal("😀",Feed(decoder,[0x80],false));
    Assert.Equal("x",Feed(decoder,[(byte)'x'],false));
    Assert.Equal("",Feed(decoder,[],true));
    Assert.IsType<InvalidOperationException>(Capture(()=>Feed(decoder,[],false)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Actual_decoder_refuses_truncated_terminal_or_invalid_scalar_without_replacement(bool truncated)
    {
    var decoder=Decoder();
    if(truncated) Assert.Equal("",Feed(decoder,[0xe2,0x82],false));
    var failure=Capture(()=>Feed(decoder,truncated ? [] : new byte[]{0xff},truncated));
    Assert.IsType<DecoderFallbackException>(failure);
    }

    [Fact]
    public async Task Failed_command_joins_actual_exit_and_stderr_before_refusing_another_request_in_same_session()
    {
    if(!OperatingSystem.IsLinux()||!File.Exists("/usr/bin/python3")) return;
    var folder=Path.Combine(Path.GetTempPath(),"strata-wire-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
    var executable=Path.Combine(folder,"wire.py"); var marker=Path.Combine(folder,"second-command");
    var script="""
#!/usr/bin/python3
import sys,struct
w=sys.stdout.buffer;r=sys.stdin.buffer
text=lambda b:struct.pack('<I',len(b))+b
def frame(t,b):w.write(struct.pack('<II',t,len(b))+b);w.flush()
def command():
 h=r.read(4)
 if len(h)!=4:return None
 n=struct.unpack('<I',h)[0];b=r.read(n)
 if len(b)!=n:return None
 return struct.unpack('<I',b[:4])[0]
frame(0,struct.pack('<I',1)+text(b'015b075079c51a7aec670ee24924f920f5e7bb2b')+struct.pack('<II',1,0)+text(b'fixture-native'))
assert command()==1
frame(4,struct.pack('<II',0,0))
assert command()==2
frame(1,struct.pack('<I',7)+text(b'\xff'))
frame(2,struct.pack('<II',0,0)+text(b'old')+struct.pack('<QQQddIII',1,1,1,1.0,1.0,0,0,0))
if command()==2:
 open(MARKER_LITERAL,'w').write('unexpected second command')
""".Replace("MARKER_LITERAL",JsonSerializer.Serialize(marker));
    StrataNativeWorker? worker=null; Task<StrataNativeWorker>? acquisition=null; Task? close=null;
    var model=new ModelIdentity("wire","fixture-model","fixture-artifact"); var binding=new Binding(model);
    var expected=new HashSet<Exception>(ReferenceEqualityComparer.Instance); Exception? primary=null;
    try
    {
    await File.WriteAllTextAsync(executable,script,new UTF8Encoding(false));
    File.SetUnixFileMode(executable,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable)));
    acquisition=StrataNativeWorker.CreateAfterPublicationAsync(Task.CompletedTask,new(executable,hash),binding,CancellationToken.None);
    worker=await acquisition;
    var request=new OllamaChatRequest(model.ModelId,[new OllamaMessage("user","same request")],Haven.Core.EffortLevel.Medium);
    var first=worker.CompleteOriginalAsync(request,CancellationToken.None);
    Assert.NotNull(await CaptureAsync(first).WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None)); Assert.True(first.IsFaulted);
    Assert.Contains(Causes(first.Exception!),cause=>cause is DecoderFallbackException);
    Remember(first.Exception!,expected);
    var originalDecoder=Causes(first.Exception!).First(cause=>cause is DecoderFallbackException);
    var exit=(Task)typeof(StrataNativeWorker).GetField("_processExit",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(worker)!;
    var stderr=(Task)typeof(StrataNativeWorker).GetField("_stderr",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(worker)!;
    Assert.True(exit.IsCompletedSuccessfully); Assert.True(stderr.IsCompletedSuccessfully);
    var second=worker.CompleteOriginalAsync(request,CancellationToken.None);
    Assert.NotNull(await CaptureAsync(second)); Assert.True(second.IsFaulted);
    Remember(second.Exception!,expected);
    var originalRefusal=Causes(second.Exception!).First(cause=>cause is InvalidOperationException);
    Assert.False(File.Exists(marker));
    close=worker.DisposeAsync().AsTask(); Assert.NotNull(await CaptureAsync(close));
    Assert.Contains(Causes(close.Exception!),cause=>ReferenceEquals(cause,originalDecoder));
    Assert.Contains(Causes(close.Exception!),cause=>ReferenceEquals(cause,originalRefusal));
    Assert.Equal(1,binding.Closed);
    }
    catch(Exception error) { primary=error; }
    finally
    {
    var cleanup=new List<Exception>(); var failures=new List<Exception>();
    if(primary is not null) failures.Add(primary);
    if(worker is not null) {
        try { close??=worker.DisposeAsync().AsTask(); } catch(Exception error) { cleanup.Add(error); }
        if(close is not null) await Observe(close,cleanup);
    }
    if(acquisition is not null) await Observe(acquisition,cleanup);
    try { Directory.Delete(folder,true); } catch(Exception error) { cleanup.Add(error); }
    foreach(var cause in cleanup) if(!Expected(cause,expected)) {
        foreach(var original in Causes(cause)) if(!failures.Any(value=>ReferenceEquals(value,original))) failures.Add(original);
    }
    if(failures.Count==1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
    if(failures.Count>1) throw new AggregateException(failures);
    }
    }

    private sealed class Binding(ModelIdentity model):StrataOriginalModelLease
    {
    public int Closed;
    public override InferenceModelRequirements Requirements { get; }=new(model,"fixture-artifact","fixture-architecture","fixture-family","Safetensors","Q4",new HashSet<string>{"streaming"},0,0,128,NativeRegistration:"fixture-native");
    public override string OriginalCheckpointDirectory=>"/synthetic-wire-model";
    public override IReadOnlyList<int> ActualCudaDeviceIndices=>[0];
    public override void DemandCurrentOriginalBinding() { if(Closed!=0) throw new InvalidOperationException("Synthetic binding retired."); }
    public override ValueTask DisposeAsync() { Closed++; return ValueTask.CompletedTask; }
    }
    private static IEnumerable<Exception> Causes(Exception original)
    {
        yield return original;
        if(original is AggregateException group) foreach(var child in group.InnerExceptions) foreach(var cause in Causes(child)) yield return cause;
        else if(original.InnerException is { } child) foreach(var cause in Causes(child)) yield return cause;
    }
    private static void Remember(Exception original,HashSet<Exception> expected)
    { foreach(var cause in Causes(original)) expected.Add(cause); }
    private static bool Expected(Exception original,HashSet<Exception> expected)
        =>expected.Contains(original)||(original is AggregateException group&&group.InnerExceptions.Count!=0&&group.InnerExceptions.All(child=>Expected(child,expected)));
    private static async Task Observe(Task original,List<Exception> errors)
    {
        try { await original; } catch(Exception error) { errors.Add(error); }
        if(original.Exception is { } group) foreach(var cause in Causes(group)) if(!errors.Any(value=>ReferenceEquals(value,cause))) errors.Add(cause);
    }
    private static object Decoder()=>Activator.CreateInstance(typeof(StrataNativeWorker).GetNestedType("TokenDecoder",BindingFlags.NonPublic)!,nonPublic:true)!;
    private static string Feed(object decoder,byte[] bytes,bool terminal)
    {
    try { return (string)decoder.GetType().GetMethod("Feed")!.Invoke(decoder,[bytes,terminal])!; }
    catch(TargetInvocationException error) when(error.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static Exception? Capture(Action body) { try { body(); return null; } catch(Exception error) { return error; } }
    private static async Task<Exception?> CaptureAsync(Task actual) { try { await actual; return null; } catch(Exception error) { return error; } }
}
