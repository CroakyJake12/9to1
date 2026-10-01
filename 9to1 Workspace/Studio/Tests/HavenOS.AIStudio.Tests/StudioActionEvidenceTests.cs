using HavenOS.AIStudio;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioActionEvidenceTests
{
    [Theory]
    [InlineData("[{\"Title\":\"Discussed files.delete without calling it\",\"Succeeded\":true}]")]
    [InlineData("[]")]
    [InlineData("malformed files.delete")]
    [InlineData("{\"complete\":true,\"ActionId\":\"files.delete\",\"Status\":\"Completed\"}")]
    public async Task Prose_legacy_empty_malformed_or_self_asserted_activity_cannot_prove_presence_or_absence(string activity)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-studio-action-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var api = new AIStudioApi(new JsonStudioProjectStore(root), new FictionalRuntime(activity),
                new UnavailableCanonicalAgentBuilderAdapter(), new JsonStudioResourceStore(root),
                new UnavailableStudioPermissionSimulationAdapter(), new UnavailableStudioContextInspectorAdapter(),
                new UnavailableStudioReplayAdapter());
            var project = (await api.CreateHarnessFromTemplateAsync("harness.assistant.v1", "Evidence boundary")).Value!;
            var suite = new TestSuite(Guid.NewGuid(), "Invocation evidence", project.ProjectId.ToString(), "Harness", project.Version,
                [new StudioTestCase(Guid.NewGuid(), "Unknown invocation stream", "fixture", "{}",
                    [new("presence", "actionCalled", "$", "\"files.delete\""), new("absence", "actionNotCalled", "$", "\"files.delete\"")],
                    project.Version, "Explicitly fictional runtime fixture; no tool dispatch")], null, 1);
            var resource = (await api.CreateTestSuiteAsync(suite.Name, suite)).Value!;
            var result = (await api.RunTestSuiteAsync(resource.ResourceId)).Value!.Results.Single();
            Assert.Equal("Failed", result.Status);
            Assert.All(result.Assertions, assertion =>
            {
                Assert.False(assertion.Passed);
                Assert.Contains("ActionEvidenceUnavailable", assertion.Message);
                Assert.Equal(activity, assertion.ActualJson);
            });
            Assert.NotNull(result.ReproductionJson);

            var evaluation = new EvaluationSuite(Guid.NewGuid(), "No false action facts", project.ProjectId.ToString(), "Harness", project.Version,
                [new EvaluationCase(Guid.NewGuid(), "Unknown actions", "fixture", "{}", null, null,
                    ["files.delete"], ["files.delete"], null, null, null, "Explicitly fictional runtime fixture", project.Version)],
                [], null, null, DateTimeOffset.UtcNow, 1);
            var evalResource = (await api.CreateEvaluationSuiteAsync(evaluation.Name, evaluation)).Value!;
            var evalResult = (await api.RunEvaluationAsync(evalResource.ResourceId)).Value!.Results.Single();
            Assert.Equal("Failed", evalResult.Status);
            Assert.Equal(2, evalResult.DeterministicFacts.Count);
            Assert.All(evalResult.DeterministicFacts, fact => Assert.Contains("ActionEvidenceUnavailable", fact));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // Tests the evidence boundary only; this adapter never represents a genuine execution.
    private sealed class FictionalRuntime(string activity) : IStudioRuntimeAdapter
    {
        public Task<StudioResult<StudioRun>> RunHarnessAsync(StudioProject project, string input, string? profile, CancellationToken ct) =>
            Task.FromResult(StudioResult<StudioRun>.Success(new(Guid.NewGuid(), project.ProjectId.ToString(), "Succeeded", "{}",
                "fictional-model", "fictional-provider", "{}", activity, "{}", "{}", null, null, null, null, null, project.Version, profile)));
        public Task<StudioResult<StudioRun>> RunPlaygroundAsync(PlaygroundConfiguration config, string input, CancellationToken ct) =>
            Task.FromResult(StudioResult<StudioRun>.Failure("RuntimeUnavailable", "Fictional fixture does not dispatch."));
        public Task<StudioResult<InstallReceipt>> InstallToolAsync(StudioProject project, CancellationToken ct) =>
            Task.FromResult(StudioResult<InstallReceipt>.Failure("RuntimeUnavailable", "Fictional fixture does not install."));
    }
}
