using System.Text.Json;
using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

public sealed partial class WorkspaceTaskRunToolOwnerTests
{
    [Fact]
    public async Task Dev_observes_whole_original_file_while_agent_output_keeps_its_limit()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
            var text = new string('x', 1_000_123) + "actual tail";
            File.WriteAllText(Path.Combine(rig.Root, "document.txt"), text);
            var call = Call("read_file", new { path = "document.txt" }); var prep = await rig.PrepareAsync(call);
            var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct => rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct), default));
            var result = await run;
            Assert.Equal(text, result.OriginalResult.OriginalReadText);
            Assert.NotEqual(result.OriginalResult.OriginalReadText, result.OriginalResult.Output);
            Assert.DoesNotContain("actual tail", result.OriginalResult.Output);
            Assert.True(WorkspaceToolRuntime.IsIssuedOriginalResult((IWorkspaceToolActionPreparation)prep, result.OriginalResult));
            Assert.Null(result.OwnerReceiptReference);
            var ack = await rig.Coordinator.RecordObservedActionOutcomeAsync(prep, result, default);
            await rig.Owner.RetireAcknowledgedOriginalAsync(prep, ack, default);
        });
    }
    [Fact]
    public async Task Copying_full_read_observation_cannot_forge_an_original_runtime_result()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
            File.WriteAllText(Path.Combine(rig.Root, "document.txt"), "actual text");
            var call = Call("read_file", new { path = "document.txt" }); var prep = await rig.PrepareAsync(call);
            var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, async ct =>
                (await rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct)) with { OriginalReadText = "forged editor text" }, default));
            var error = await Record.ExceptionAsync(() => run); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
            Assert.Same(run, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new InvalidOperationException("No replay"), default));
        });
    }
    [Fact]
    public void Host_observations_are_not_implicitly_serialized_into_agent_payloads()
    {
        var result = new WorkspaceToolResult(new(Guid.NewGuid(), "synthetic", "synthetic", true, TimeSpan.Zero, DateTimeOffset.UnixEpoch), "bounded")
        { OriginalReadText = "host-only-full-text", OriginalProcessResult = new(0, "host-only-stdout", "", TimeSpan.Zero, false) };
        var wire = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("host-only-full-text", wire); Assert.DoesNotContain("host-only-stdout", wire);
        Assert.DoesNotContain("OriginalReadText", wire); Assert.DoesNotContain("OriginalProcessResult", wire);
    }
}
