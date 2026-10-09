using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Same_name_view_is_unknown_schema_not_a_legacy_absence_recall_fallback() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var original = await rig.AddAsync(binding, "preserved ordinary preference", scope: "global");
        var before = await rig.CaptureMemoryAsync();
        await ExecuteRevisionFixture(rig, "CREATE VIEW retrieval_source_states AS SELECT 1 AS retained_fixture_value;");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Recall(rig, original.Id, "ordinary"));
        Assert.Contains("schema object kind", failure.Message);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
        Assert.Equal(KnowledgeRecordStatus.Active, (await rig.Knowledge.GetAsync(original.Id, Token))!.Status);
    });
}
