using System.Reflection;
using Haven.Application;
using Haven.Core;
using Dulche.Runtime;

namespace Haven.Infrastructure.Tests;

public sealed partial class ResilientProviderRoutingRecoveryTests
{
    [Fact]
    public async Task Arbitrary_local_provider_named_llama_cpp_cannot_issue_an_observed_checkpoint_selection()
    {
        var catalogueCalls = 0;
        var fake = new Provider("llama-cpp", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        fake.CatalogueBody = _ => { catalogueCalls++; return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([fake.Descriptor]); };
        var fixture = await CanonicalFixture.CreateAsync([fake]);
        var router = CreateCheckpointSelectionRouter([fake], fixture);
        var request = Tools(fake) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var original = router.ChatWithToolsAsync(request, CancellationToken.None);
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => original);
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(router.TryGetOriginalFinalRequestFailure(request, outward));
            await fixture.Frames.AwaitSettlementAsync(fixture.Task.TaskId, fixture.Task.ExecutionId,
                binding.OriginalAdmission.AttemptId, CancellationToken.None);
            var current = await fixture.CurrentAsync();
            var before = catalogueCalls;
            var actualSelection = router.SelectLocalForOriginalToolCheckpointAsync(binding, current, CancellationToken.None);
            Assert.Null(await actualSelection);
            Assert.True(actualSelection.IsCompletedSuccessfully);
            Assert.Equal(before, catalogueCalls);
            Assert.Equal(1, fake.ToolCalls);
            Assert.True(original.IsFaulted);
            Assert.Equal(TaskExecutionLifecycle.Suspended, (await fixture.CurrentAsync()).State);
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Checkpoint_selection_cannot_start_settlement_or_close_the_lease_as_a_side_effect()
    {
        var fake = new Provider("llama-cpp", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        var fixture = await CanonicalFixture.CreateAsync([fake]);
        var router = CreateCheckpointSelectionRouter([fake], fixture);
        var request = Tools(fake) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => router.ChatWithToolsAsync(request, CancellationToken.None));
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(router.TryGetOriginalFinalRequestFailure(request, outward));
            Assert.Null(await router.SelectLocalForOriginalToolCheckpointAsync(binding, await fixture.CurrentAsync(), CancellationToken.None));
            Assert.Equal(0, Assert.Single(fixture.Authority.Leases).Disposals);
            Assert.Null(router.TryGetOriginalRequestFailure(request, binding.OriginalAdmission, outward));
            Assert.Equal(1, fake.ToolCalls);
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Copied_final_failure_cannot_admit_a_checkpoint_selection_driver()
    {
        var fake = new Provider("llama-cpp", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        var fixture = await CanonicalFixture.CreateAsync([fake]);
        var router = CreateCheckpointSelectionRouter([fake], fixture);
        var request = Tools(fake) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => router.ChatWithToolsAsync(request, CancellationToken.None));
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(router.TryGetOriginalFinalRequestFailure(request, outward));
            var copied = (TaskRunOriginalFinalRequestFailure)typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(binding, null)!;
            var current = await fixture.CurrentAsync();
            Action refused = () => { _ = router.SelectLocalForOriginalToolCheckpointAsync(copied, current, CancellationToken.None); };
            Assert.Throws<UnauthorizedAccessException>(refused);
            Assert.Equal(1, fake.ToolCalls);
            Assert.Equal(0, Assert.Single(fixture.Authority.Leases).Disposals);
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    [Fact]
    public async Task Supplied_stale_checkpoint_row_cannot_replace_the_actual_fresh_same_run_read()
    {
        var fake = new Provider("llama-cpp", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        var fixture = await CanonicalFixture.CreateAsync([fake]);
        var router = CreateCheckpointSelectionRouter([fake], fixture);
        var request = Tools(fake) with { ExecutionContext = fixture.Context(fixture.Task) };
        var failures = new List<Exception>();
        try
        {
            var outward = await Assert.ThrowsAsync<InvalidOperationException>(() => router.ChatWithToolsAsync(request, CancellationToken.None));
            var binding = Assert.IsAssignableFrom<TaskRunOriginalFinalRequestFailure>(router.TryGetOriginalFinalRequestFailure(request, outward));
            await fixture.Frames.AwaitSettlementAsync(fixture.Task.TaskId, fixture.Task.ExecutionId,
                binding.OriginalAdmission.AttemptId, CancellationToken.None);
            var current = await fixture.CurrentAsync();
            var actual = router.SelectLocalForOriginalToolCheckpointAsync(binding,
                current with { PersistenceRevision = current.PersistenceRevision - 1 }, CancellationToken.None);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actual);
            Assert.True(actual.IsFaulted);
            Assert.Equal(1, fake.ToolCalls);
            Assert.Equal(current.PersistenceRevision, (await fixture.CurrentAsync()).PersistenceRevision);
        }
        catch (Exception error) { failures.Add(error); }
        finally { await CollectFinalFailureOriginalAsync(fixture.Frames.CloseAndDrainAsync(), failures); }
        ThrowFinalFailureControlErrors(failures);
    }

    private static ResilientProviderRoutingModelClient CreateCheckpointSelectionRouter(IReadOnlyList<Provider> actual,
        CanonicalFixture fixture)
    {
        var registry = new ModelProviderRegistry(actual);
        var privacy = new Privacy();
        var configurations = new Configurations(actual.Select(provider => new ProviderConfiguration(provider.Id,
            ModelProviderKind.Ollama, provider.Id, "https://example.invalid/", true, true, true,
            new Dictionary<string, string>(), DateTimeOffset.UnixEpoch)).ToArray());
        return new(new ProviderRoutingModelClient(new EmptyLocal(), registry, privacy), registry, configurations, privacy,
            executionEvents: fixture.Events, taskCoordinator: fixture.Coordinator, routeCapture: fixture.Authority,
            originalFrames: fixture.Frames, catalogueEligibility: new ModelRouteResolver(registry));
    }
}
