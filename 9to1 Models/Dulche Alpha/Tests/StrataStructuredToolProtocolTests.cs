using System.Collections.ObjectModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Codec and synthetic actual pipe controls only. Invented fixture CUDA/registration
/// and inventory never prove protected native load, GPU capability, Task authority or installation.</summary>
public sealed class StrataStructuredToolProtocolTests
{
    [Fact]
    public void Exact_schema_history_and_parallel_results_remain_correlated_without_execution()
    {
        var request = Request() with { Messages = [new("user", "inspect"),
            new("assistant", "", [Call("first"), Call("second")]),
            new("tool", "result one", ToolName: "fixture.echo"), new("tool", "result two", ToolName: "fixture.echo")] };
        var captured = StrataStructuredToolCodec.Capture(request);
        Assert.Same(request.Options, captured.Chat.Options); Assert.Same(request.ExecutionContext, captured.Chat.ExecutionContext);
        Assert.Equal(request.Model, captured.Chat.Model); Assert.False(captured.Chat.EnableTools);
        Assert.Contains(StrataStructuredToolCodec.Protocol, captured.Chat.SystemPrompt);
        Assert.Contains("parameters", captured.Chat.SystemPrompt);
        using var first = JsonDocument.Parse(captured.Chat.Messages[2].Content);
        using var second = JsonDocument.Parse(captured.Chat.Messages[3].Content);
        Assert.Equal("first", first.RootElement.GetProperty("tool_call_id").GetString());
        Assert.Equal("second", second.RootElement.GetProperty("tool_call_id").GetString());
        Assert.Equal("result two", second.RootElement.GetProperty("content").GetString());
        Assert.Equal(new[] { "", "", "fixture.echo", "fixture.echo" }, captured.MessageNames);
    }

    [Fact]
    public void Actual_envelope_produces_frozen_typed_proposals_and_empty_call_text_is_valid()
    {
        var captured = StrataStructuredToolCodec.Capture(Request());
        var result = StrataStructuredToolCodec.ParseResponse(captured,
            """{"content":"proposed","tool_calls":[{"id":"actual-id","name":"fixture.echo","arguments":{"value":"actual"}}]}""");
        Assert.Equal("proposed", result.Content); var call = Assert.Single(result.ToolCalls);
        Assert.Equal("actual-id", call.Id); Assert.Equal("fixture.echo", call.Name);
        Assert.Equal("actual", call.Arguments["value"].GetString());
        Assert.Equal("no call", StrataStructuredToolCodec.ParseResponse(captured, """{"content":"no call","tool_calls":[]}""").Content);
    }

    [Theory]
    [InlineData("ordinary text mentioning fixture.echo")]
    [InlineData("```json\n{\"content\":\"\",\"tool_calls\":[]}\n```")]
    [InlineData("{\"content\":\"\",\"content\":\"duplicate\",\"tool_calls\":[]}")]
    [InlineData("{\"content\":\"\",\"tool_calls\":[],\"grant\":true}")]
    [InlineData("{\"content\":\"\",\"tool_calls\":[{\"id\":\"x\",\"name\":\"foreign\",\"arguments\":{}}]}")]
    [InlineData("{\"content\":\"\",\"tool_calls\":[{\"id\":\"x\",\"name\":\"fixture.echo\",\"arguments\":[]}]} ")]
    [InlineData("{\"content\":\"\",\"tool_calls\":[{\"id\":\"x\",\"name\":\"fixture.echo\",\"arguments\":{}}]}")]
    [InlineData("{\"content\":\"\",\"tool_calls\":[{\"id\":\"x\",\"name\":\"fixture.echo\",\"arguments\":{\"value\":1,\"value\":2}}]}")]
    [InlineData("{\"content\":\"\",\"tool_calls\":[{\"id\":\"x\",\"name\":\"fixture.echo\",\"arguments\":{\"value\":1}},{\"id\":\"x\",\"name\":\"fixture.echo\",\"arguments\":{\"value\":2}}]}")]
    public void Arbitrary_text_foreign_fields_names_ids_or_arguments_never_become_tool_authority(string response)
        => Assert.NotNull(Record.Exception(() => StrataStructuredToolCodec.ParseResponse(StrataStructuredToolCodec.Capture(Request()), response)));

    [Theory]
    [InlineData("", "", false, "{\"content\":\"plain\",\"tool_calls\":[]}")]
    [InlineData("<reason>", "</reason>", false, "<reason>declared reasoning</reason>{\"content\":\"plain\",\"tool_calls\":[]}")]
    [InlineData("<reason>", "</reason>", true, "prompt-opened reasoning</reason>{\"content\":\"plain\",\"tool_calls\":[]}")]
    public void Only_actual_declared_closed_or_prompt_opened_reasoning_precedes_the_strict_final_envelope(
        string open, string close, bool openedByPrompt, string actual)
    {
        var result = StrataStructuredToolCodec.ParseResponse(StrataStructuredToolCodec.Capture(Request()), actual,
            new(open, close, openedByPrompt));
        Assert.Equal("plain", result.Content); Assert.Empty(result.ToolCalls);
    }

    [Theory]
    [InlineData("<reason>", "", false, "{\"content\":\"plain\",\"tool_calls\":[]}")]
    [InlineData("", "", true, "{\"content\":\"plain\",\"tool_calls\":[]}")]
    [InlineData("<reason>", "</reason>", false, "<foreign>text</reason>{\"content\":\"plain\",\"tool_calls\":[]}")]
    [InlineData("<reason>", "</reason>", false, "<reason>never closed{\"content\":\"plain\",\"tool_calls\":[]}")]
    [InlineData("<reason>", "</reason>", true, "<reason>duplicate opening</reason>{\"content\":\"plain\",\"tool_calls\":[]}")]
    [InlineData("<reason>", "</reason>", true, "prompt-opened never closed{\"content\":\"plain\",\"tool_calls\":[]}")]
    public void Missing_malformed_foreign_or_orphan_declared_reasoning_never_becomes_a_tool_result(
        string open, string close, bool openedByPrompt, string actual)
        => Assert.NotNull(Record.Exception(() => StrataStructuredToolCodec.ParseResponse(
            StrataStructuredToolCodec.Capture(Request()), actual, new(open, close, openedByPrompt))));

    [Fact]
    public void Orphan_results_ambiguous_schemas_and_reused_history_ids_refuse()
    {
        Assert.Throws<InvalidDataException>(() => StrataStructuredToolCodec.Capture(Request() with
        { Messages = [new("user", "u"), new("tool", "orphan", ToolName: "fixture.echo")] }));
        using var ambiguous = JsonDocument.Parse("""{"type":"object","type":"array"}""");
        Assert.Throws<InvalidDataException>(() => StrataStructuredToolCodec.Capture(Request() with
        { Tools = [Request().Tools[0] with { InputSchema = ambiguous.RootElement }] }));
        var captured = StrataStructuredToolCodec.Capture(Request() with { Messages =
            [new("user", "u"), new("assistant", "", [Call("old")]), new("tool", "result", ToolName: "fixture.echo")] });
        Assert.Throws<InvalidDataException>(() => StrataStructuredToolCodec.ParseResponse(captured,
            """{"content":"","tool_calls":[{"id":"old","name":"fixture.echo","arguments":{"value":"v"}}]}"""));
    }

    [Fact]
    public void Captured_schema_and_history_do_not_follow_mutated_inputs()
    {
        var properties = new Dictionary<string, object> { ["value"] = new { type = "string" } };
        var required = new List<string> { "value" }; var messages = new List<OllamaToolTurn> { new("user", "original") };
        var request = Request() with { Tools = [new("fixture.echo", "original description", properties, required)], Messages = messages };
        var capture = StrataStructuredToolCodec.Capture(request);
        properties.Clear(); required.Clear(); messages[0] = new("user", "replacement");
        Assert.Equal("original", capture.Chat.Messages[0].Content);
        Assert.Equal("value", capture.Schemas["fixture.echo"].GetProperty("required")[0].GetString());
        Assert.Throws<InvalidDataException>(() => StrataStructuredToolCodec.ParseResponse(capture,
            """{"content":"","tool_calls":[{"id":"new","name":"fixture.echo","arguments":{}}]}"""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Synthetic_actual_worker_tool_terminal_probe_text_and_original_close_are_distinct(bool mismatchedTerminal)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3")) return;
        var folder = Path.Combine(Path.GetTempPath(), "strata-tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "wire.py");
        var script = """
#!/usr/bin/python3
import sys,struct,json,re
w=sys.stdout.buffer;r=sys.stdin.buffer
mismatch=MISMATCH_LITERAL;tool_generations=0
text=lambda s:struct.pack('<I',len(s.encode()))+s.encode()
def frame(t,b):w.write(struct.pack('<II',t,len(b))+b);w.flush()
def command():
 h=r.read(4)
 if len(h)!=4:return None
 n=struct.unpack('<I',h)[0];b=r.read(n)
 if len(b)!=n:raise RuntimeError('truncated command')
 return b
def generation(body,tool):
 global tool_generations
 if tool:tool_generations+=1
 offset=8 if tool else 4
 def number():
  nonlocal offset
  value=struct.unpack_from('<I',body,offset)[0];offset+=4;return value
 def string():
  nonlocal offset
  n=number();value=body[offset:offset+n].decode();offset+=n;return value
 count=number();messages=[]
 for i in range(count):
  role=number();content=string();name=string();assert number()==0;messages.append(content)
  if role==3:assert name=='fixture.echo' and json.loads(content)['tool_call_id']=='history'
 if tool:
  assert struct.unpack_from('<I',body,4)[0]==1
  prompt='\n'.join(messages)
  if 'dulche_transport_probe' in prompt:
   nonce=re.search(r'with nonce ([0-9a-f]{32})',prompt).group(1)
   output=json.dumps({'content':'','tool_calls':[{'id':'probe-'+nonce,'name':'dulche_transport_probe','arguments':{'nonce':nonce}}]})
  else:output=json.dumps({'content':'proposed','tool_calls':[{'id':'actual-wire-id','name':'fixture.echo','arguments':{'value':'actual'}}]})
 else:output='ordinary stream'
 frame(1,struct.pack('<I',7)+text(output))
 terminal=struct.pack('<II',0,0)+text(output)+struct.pack('<QQQddIII',1,1,1,1.0,1.0,0,0,0)
 frame(2 if tool and mismatch and tool_generations>1 else 7 if tool else 2,(struct.pack('<I',1)+text('')+text('')+struct.pack('<I',0) if tool else b'')+terminal)
frame(0,struct.pack('<I',1)+text('015b075079c51a7aec670ee24924f920f5e7bb2b')+struct.pack('<II',1,0)+text('fixture-native'))
assert struct.unpack_from('<I',command())[0]==1
frame(4,struct.pack('<II',0,0))
while True:
 body=command()
 if body is None:break
 op=struct.unpack_from('<I',body)[0]
 if op==3:frame(3,b'');break
 assert op in (2,4);generation(body,op==4)
""".Replace("MISMATCH_LITERAL", mismatchedTerminal ? "True" : "False");
        StrataNativeWorker? worker = null; Task<StrataNativeWorker>? acquisition = null;
        var errors = new List<Exception>(); Binding? binding = null;
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        try
        {
            await File.WriteAllTextAsync(executable, script, new UTF8Encoding(false));
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable))).ToLowerInvariant();
            binding = new(sha); acquisition = StrataNativeWorker.CreateAfterPublicationAsync(Task.CompletedTask, new(executable, sha), binding, CancellationToken.None);
            worker = await acquisition; Assert.False(worker.OriginalStructuredToolsAvailable);
            var descriptor = new ProviderModelDescriptor("wire", true, new("fixture-model", 1, "fixture", "", "",
                new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools }, DateTimeOffset.UtcNow), 128, "fixture-model");
            Assert.Throws<ArgumentException>(() => new StrataRawModelProvider("wire", worker, descriptor));
            Assert.True(await worker.ProbeOriginalStructuredToolsAsync(CancellationToken.None)); Assert.True(worker.OriginalStructuredToolsAvailable);
            var raw = new StrataRawModelProvider("wire", worker, descriptor);
            Assert.True(Assert.Single(await raw.GetModelsAsync(CancellationToken.None)).Supports(ToolCapability.Tools));
            var continued = Request() with { Messages = [new("user", "echo"),
                new("assistant", "", [Call("history")]), new("tool", "original result", ToolName: "fixture.echo")] };
            var actual = raw.ChatWithToolsAsync(continued, CancellationToken.None);
            if (mismatchedTerminal)
            {
                Assert.NotNull(await Record.ExceptionAsync(async () => { await actual; })); Assert.True(actual.IsFaulted);
                foreach (var cause in Causes(actual.Exception!)) expected.Add(cause);
                var original = Causes(actual.Exception!).First(cause => cause is InvalidDataException);
                var close = worker.DisposeAsync().AsTask();
                Assert.NotNull(await Record.ExceptionAsync(async () => { await close; }));
                Assert.Contains(Causes(close.Exception!), cause => ReferenceEquals(cause, original));
                foreach (var cause in Causes(close.Exception!)) expected.Add(cause);
                Assert.False(worker.OriginalStructuredToolsAvailable);
            }
            else
            {
                var response = await actual;
                Assert.Equal("actual-wire-id", Assert.Single(response.ToolCalls).Id);
                Assert.Equal("actual", response.ToolCalls[0].Arguments["value"].GetString());
                Assert.Equal("ordinary stream", await worker.CompleteOriginalAsync(new("fixture-model", [new("user", "text")], EffortLevel.Medium), CancellationToken.None));
                await worker.DisposeAsync();
            }
            Assert.Equal(1, binding.Closed);
            var exit = (Task)typeof(StrataNativeWorker).GetField("_processExit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(worker)!;
            var stderr = (Task)typeof(StrataNativeWorker).GetField("_stderr", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(worker)!;
            Assert.True(exit.IsCompletedSuccessfully); Assert.True(stderr.IsCompletedSuccessfully);
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            if (worker is not null) try { await worker.DisposeAsync(); } catch (Exception error) {
                if (!Causes(error).All(cause => expected.Contains(cause))) errors.Add(error); }
            if (acquisition is not null) try { await acquisition; } catch (Exception error) { errors.Add(error); }
            try { Directory.Delete(folder, true); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }

    private static IEnumerable<Exception> Causes(Exception original)
    {
        yield return original;
        if (original is AggregateException group) foreach (var child in group.InnerExceptions) foreach (var cause in Causes(child)) yield return cause;
        else if (original.InnerException is { } child) foreach (var cause in Causes(child)) yield return cause;
    }

    private static OllamaToolRequest Request() => new("fixture-model", [new("user", "echo")],
        [new("fixture.echo", "echo a value", new Dictionary<string, object> { ["value"] = new { type = "string" } }, ["value"])],
        EffortLevel.Medium, Options: new(0.7, 128, 24));
    private static OllamaToolCall Call(string id) => new("fixture.echo",
        new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("v") }), id);
    private sealed class Binding(string sha) : StrataOriginalModelLease
    {
        public int Closed;
        public override InferenceModelRequirements Requirements { get; } = new(new("wire", "fixture-model", "fixture-artifact"),
            "fixture-artifact", "fixture-architecture", "fixture-family", "Safetensors", "Q4", new HashSet<string> { "Text", "Tools" }, 0, 0, 128, NativeRegistration: "fixture-native");
        public override string OriginalCheckpointDirectory => "/synthetic-wire-model";
        public override IReadOnlyList<int> ActualCudaDeviceIndices => [0];
        public override InferenceEngineSupport? OriginalBuildSupport { get; } = new(InferenceEngine.Strata,
            "strata/015b075079c51a7aec670ee24924f920f5e7bb2b/abi1/" + sha, true, null,
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string> { "Tools" },
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), RequiresCuda: true, NativeRegistrations: new HashSet<string> { "fixture-native" });
        public override void DemandCurrentOriginalBinding() { if (Closed != 0) throw new InvalidOperationException("Synthetic binding retired."); }
        public override ValueTask DisposeAsync() { Closed++; return ValueTask.CompletedTask; }
    }
}
