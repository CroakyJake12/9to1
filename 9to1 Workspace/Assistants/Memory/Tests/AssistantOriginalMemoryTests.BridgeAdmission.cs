using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Configured_memory_bridge_constructs_before_admission_and_observes_actual_owner_during_reads() => RunAsync(async rig =>
    {
        // InitializeAsync already constructed the SAME bridge after genuine Home import.
        // Its membership metadata must not execute the admitted memory callback.
        var before = await rig.CaptureMemoryAsync();
        var binding = await rig.CreateAsync(new() { Name = "Admission boundary", Memory = new(false) });
        var configured = Assert.Single(binding.Definition.Capabilities, value => value.Feature == "Own Assistant memory");
        Assert.Equal(AssistantSupportState.RequiresAuthorization, configured.State);

        var expected = rig.Memory.ObserveOriginalMemoryCapability();
        Assert.NotEqual(configured.Reason, expected.Reason);
        var catalogue = await rig.Bridge.ListAsync(Token);
        Assert.Single(catalogue.Definitions, value => value.Identity == binding.Definition.Identity);
        Assert.Equal(expected, Assert.Single(catalogue.HostCapabilities, value => value.Feature == expected.Feature));
        var work = await rig.Bridge.ReadWorkAsync(binding, Token);
        Assert.Null(work.CanonicalTask); Assert.Null(work.Controls);
        Assert.Equal(expected, Assert.Single(work.Capabilities, value => value.Feature == expected.Feature));
        Assert.Equal(before, await rig.CaptureMemoryAsync());

        var close = rig.Bridge.CloseAndDrainAsync();
        Assert.Same(close, rig.Bridge.CloseAndDrainAsync());
        await close;
        Assert.True(close.IsCompletedSuccessfully);
        // RunAsync independently joins the real memory, ordinary, WRITE, SQLite and Den owners.
    });
}
