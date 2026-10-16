using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HavenOS.Home.Core;
using NineToOne.Dulche.Context;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public async Task Slow_Ucml_reply_does_not_start_an_auxiliary_ETA_request_and_settles_cleanly()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var local = new UcmlDispatchProvider("llama-cpp", true)
        {
            BeforeStream = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); }
        };
        var rig = DispatchRig(local);
        try
        {
            await rig.InitializeAsync(true);
            var binding = await rig.CreateAsync(DispatchConfiguration(["communication"]));
            await ImportForDispatchAsync(rig, binding, UcmlFixture(body: "Explain examples with fictional marker quiet-iris."));
            var choice = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
            var observation = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Explain examples", choice.Model, ProviderId: choice.ProviderId), Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            // Cross the real tracker deadline while the real scoped ordinary producer is
            // still live. A synchronous ETA failure would remain in its source ledger.
            await Task.Delay(ChatExecutionTracker.EtaDelay + TimeSpan.FromMilliseconds(250), Token);
            Assert.Equal(0, local.CompletionCalls);
            Assert.True(Assert.Single(local.Requests).Options!.RequireOriginalPrivateContext);
            release.TrySetResult();
            await observation.WaitAsync(Token); await observation.CloseAndDrainAsync();
            Assert.Single(local.Requests);
        }
        finally { release.TrySetResult(); await CloseDispatchRigAsync(rig); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ucml_Ask_waits_for_actual_Home_and_decline_continues_without_private_context(bool accept)
    {
        var local = new UcmlDispatchProvider("llama-cpp", true); var rig = DispatchRig(local);
        try
        {
            await rig.InitializeAsync(true);
            var configuration = DispatchConfiguration([]);
            configuration = configuration with { Memory = configuration.Memory with { UcmlAskCategories = ["communication"] } };
            var binding = await rig.CreateAsync(configuration);
            var document = UcmlFixture(body: "Explain examples with the ask-only marker silver-iris.");
            await ImportForDispatchAsync(rig, binding, document);
            var choice = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
            var observation = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Explain examples", choice.Model, ProviderId: choice.ProviderId), Token);
            var finished = observation.WaitAsync(Token);
            var requestId = await DecideUcmlAskAsync(rig, finished, accept ? HomeApprovalChoice.Accept : HomeApprovalChoice.Decline,
                request =>
                {
                    Assert.Empty(local.Requests); Assert.False(finished.IsCompleted);
                    Assert.Contains(request.Scope.Objects, value => value.ObjectType == "ucml-record" && value.ObjectId == document.Metadata.Id.ToString("D"));
                    Assert.DoesNotContain("silver-iris", JsonSerializer.Serialize(request));
                });
            await finished; await observation.CloseAndDrainAsync();
            var request = Assert.Single(local.Requests);
            if (accept) { Assert.Contains("silver-iris", request.SystemPrompt); Assert.True(request.Options!.RequireOriginalPrivateContext); }
            else { Assert.DoesNotContain("silver-iris", request.SystemPrompt); Assert.False(request.Options!.RequireOriginalPrivateContext); }
            Assert.DoesNotContain((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                value => value.Scope.ActionName == HomeUcmlContextApprovalOwner.Action);

            // A second request gets a different manual review even after an accepted first
            // request. A routine decline completes normally and keeps the same owner usable.
            var next = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Explain examples again", choice.Model, ProviderId: choice.ProviderId), Token);
            var nextFinished = next.WaitAsync(Token);
            var nextId = await DecideUcmlAskAsync(rig, nextFinished, HomeApprovalChoice.Decline,
                _ => Assert.Single(local.Requests));
            Assert.NotEqual(requestId, nextId);
            await nextFinished; await next.CloseAndDrainAsync();
            Assert.Equal(2, local.Requests.Count); Assert.DoesNotContain("silver-iris", local.Requests[1].SystemPrompt);
        }
        finally { await CloseDispatchRigAsync(rig); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ucml_broker_cannot_bypass_Ask_category_review(bool accept)
    {
        var rig = DispatchRig(new UcmlDispatchProvider("llama-cpp", true));
        try
        {
            await rig.InitializeAsync(true);
            var configuration = DispatchConfiguration([]);
            var binding = await rig.CreateAsync(configuration with
                { Memory = configuration.Memory with { UcmlAskCategories = ["communication"] } });
            await ImportForDispatchAsync(rig, binding, UcmlFixture(body: "Explain examples with ask broker marker silver-fern."));
            await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
            var retrieval = management.RetrieveUcmlAsync(binding, new(AssistantUcmlContextRequest.LocalApp,
                AssistantUcmlContextRequest.LocalPurpose, AssistantUcmlContextRequest.LocalRecipient, "Explain examples", ["communication"]), Token);
            await DecideUcmlAskAsync(rig, retrieval, accept ? HomeApprovalChoice.Accept : HomeApprovalChoice.Decline,
                _ => Assert.False(retrieval.IsCompleted));
            var result = await retrieval;
            if (accept) Assert.Contains("silver-fern", Assert.Single(result.Items).Markdown);
            else Assert.Empty(result.Items);
        }
        finally { await CloseDispatchRigAsync(rig); }
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("llama-cpp")]
    public async Task Ucml_approved_import_enters_actual_Home_Assistant_Chat_and_local_provider_with_full_provenance(string providerId)
    {
        var local = new UcmlDispatchProvider(providerId, true); var remote = new UcmlDispatchProvider("remote", false);
        var rig = DispatchRig(local, remote);
        try
        {
            await rig.InitializeAsync(true);
            var binding = await rig.CreateAsync(DispatchConfiguration(["communication"]));
            var document = UcmlFixture(body: "My fictional private marker is teal-orchid. Explain code using concrete examples.");
            await ImportForDispatchAsync(rig, binding, document);
            var selected = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
            var observation = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Please explain code examples", selected.Model, ProviderId: selected.ProviderId), Token);
            var events = new List<ChatStreamEvent>();
            await foreach (var item in observation.ObserveOriginalEventsAsync(Token)) events.Add(item);
            await observation.WaitAsync(Token); await observation.CloseAndDrainAsync();
            Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
            var request = Assert.Single(local.Requests);
            Assert.Contains("teal-orchid", request.SystemPrompt); Assert.Contains("fixture-message", request.SystemPrompt);
            Assert.Contains(document.Metadata.Id.ToString(), request.SystemPrompt); Assert.Contains("not instructions or permission", request.SystemPrompt);
            Assert.True(request.Options!.RequireOriginalPrivateContext); Assert.NotNull(request.Options.OriginalPrivateContext);
            Assert.Equal(0, remote.CatalogueCalls + remote.Requests.Count);
            var serialized = JsonSerializer.Serialize(request.Options);
            Assert.DoesNotContain("\"OriginalPrivateContext\":", serialized);
            var restored = JsonSerializer.Deserialize<GenerationOptions>(serialized)!;
            Assert.True(restored.RequireOriginalPrivateContext); Assert.Null(restored.OriginalPrivateContext);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => OriginalPrivateContextDispatch.ValidateAsync(restored, true, Token));
            // Route substitution is rejected at the maintained provider boundary before remote code sees data.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => DrainPrivateRequestAsync(
                new ProviderRoutingModelClient(new NoModelCalls(), new ModelProviderRegistry([local, remote]), new UcmlDispatchPrivacy()),
                request with { Model = "remote:model" }));
            Assert.Empty(remote.Requests);
        }
        finally { await CloseDispatchRigAsync(rig); }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("other")]
    [InlineData("expired")]
    public async Task Ucml_import_does_not_enable_disclosure_without_current_explicit_category_access(string policy)
    {
        var local = new UcmlDispatchProvider("ollama", true); var rig = DispatchRig(local);
        try
        {
            await rig.InitializeAsync(true);
            var configuration = DispatchConfiguration(policy == "empty" ? [] : policy == "other" ? ["education"] : ["communication"]);
            if (policy == "expired") configuration = configuration with
            { Memory = configuration.Memory with { UcmlAccessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) } };
            var binding = await rig.CreateAsync(configuration);
            await ImportForDispatchAsync(rig, binding, UcmlFixture(body: "The forbidden marker is private-orchid. Explain examples."));
            var choice = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
            var observation = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Explain examples", choice.Model, ProviderId: choice.ProviderId), Token);
            await observation.WaitAsync(Token); await observation.CloseAndDrainAsync();
            var request = Assert.Single(local.Requests);
            Assert.DoesNotContain("private-orchid", request.SystemPrompt); Assert.DoesNotContain("ucml_context", request.SystemPrompt);
            Assert.False(request.Options!.RequireOriginalPrivateContext);
        }
        finally { await CloseDispatchRigAsync(rig); }
    }

    [Fact]
    public async Task Ucml_cannot_cross_Assistant_binding_and_temporary_chat_never_receives_its_guard()
    {
        var local = new UcmlDispatchProvider("ollama", true); var rig = DispatchRig(local);
        try
        {
            await rig.InitializeAsync(true);
            var owner = await rig.CreateAsync(DispatchConfiguration(["communication"]));
            await ImportForDispatchAsync(rig, owner, UcmlFixture(body: "The owner-only marker is private-lilac. Explain examples."));
            var other = await rig.CreateAsync(DispatchConfiguration(["communication"]));
            var choice = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(other, Token));
            var observation = await rig.Bridge.SendConversationOriginalAsync(other,
                new("Explain examples", choice.Model, ProviderId: choice.ProviderId), Token);
            await observation.WaitAsync(Token); await observation.CloseAndDrainAsync();
            Assert.DoesNotContain("private-lilac", Assert.Single(local.Requests).SystemPrompt);
            // Plain temporary Chat uses its real maintained pipeline but has no source-issued
            // Assistant input, regardless of the same model and same query.
            var registry = new ModelProviderRegistry([local]); var privacy = new UcmlDispatchPrivacy();
            var router = new ResilientProviderRoutingModelClient(new(new NoModelCalls(), registry, privacy), registry,
                new UcmlDispatchConfigurations([local]), privacy);
            var chat = new ChatSessionService(rig.OriginalConversations, router, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
            chat.BindOriginalPersistentMemorySource(rig.Memory);
            var temporary = owner.Conversation with { Id = Guid.NewGuid(), IsTemporary = true };
            await foreach (var _ in chat.SendAsync(temporary, "Explain examples", choice.Model, EffortLevel.Medium,
                [], "Assistant", "", DuoMode.Solo, null, null, null, null, Token,
                generationOptions: new() { RequestedRoutingConstraints = new(false, false) })) { }
            Assert.Equal(2, local.Requests.Count); Assert.DoesNotContain("private-lilac", local.Requests[1].SystemPrompt);
            Assert.Null(local.Requests[1].Options!.OriginalPrivateContext);
        }
        finally { await CloseDispatchRigAsync(rig); }
    }

    [Fact]
    public async Task Ucml_rejection_during_actual_provider_catalogue_read_prevents_first_model_dispatch()
    {
        var local = new UcmlDispatchProvider("ollama", true); var dispatching = false; var revoked = false;
        var rig = DispatchRigWithSafety(new UcmlDispatchSafety(() => dispatching = true), local);
        try
        {
            await rig.InitializeAsync(true);
            var binding = await rig.CreateAsync(DispatchConfiguration(["communication"]));
            await ImportForDispatchAsync(rig, binding, UcmlFixture(body: "Explain examples with the revocable marker orange-lotus."));
            await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
            local.BeforeCatalogue = async () =>
            {
                if (!dispatching || revoked) return;
                revoked = true;
                var view = await management.ReadAsync(binding, Token);
                var rejection = await management.PrepareRevisionAsync(view, Assert.Single(view.Records),
                    CanonicalAssistantMemoryMutationKind.Reject, "", "", Guid.NewGuid(), Token);
                var commit = management.CommitAsync(rejection.Preview!, Token);
                await DecideMemoryRevision(rig, commit, HomeCanonicalAssistantMemoryWriteSource.RejectAction, HomeApprovalChoice.Accept);
                Assert.True((await commit).Saved);
            };
            var choice = Assert.Single(await rig.Bridge.ListAvailableModelsAsync(binding, Token));
            var observation = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Explain examples", choice.Model, ProviderId: choice.ProviderId), Token);
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => observation.WaitAsync(Token));
            Assert.True(revoked); Assert.Empty(local.Requests);
            Assert.Contains(DispatchLeaves(refusal), leaf => leaf.Message.Contains("changed or was revoked", StringComparison.Ordinal));
            await Assert.ThrowsAnyAsync<Exception>(() => observation.CloseAndDrainAsync());
        }
        finally { await CloseDispatchRigAsync(rig, "changed or was revoked"); }
    }

    [Fact]
    public async Task Ucml_local_fallback_revalidates_the_same_context_and_never_discovers_remote()
    {
        var first = new UcmlDispatchProvider("ollama", true) { FailStream = true };
        var second = new UcmlDispatchProvider("second", true); var remote = new UcmlDispatchProvider("remote", false);
        var rig = DispatchRig(first, second, remote);
        try
        {
            await rig.InitializeAsync(true);
            var configuration = DispatchConfiguration(["communication"]);
            var binding = await rig.CreateAsync(configuration with { Model = configuration.Model with { AllowFallback = true } });
            await ImportForDispatchAsync(rig, binding, UcmlFixture(body: "Explain examples with fallback marker blue-fern."));
            var choice = (await rig.Bridge.ListAvailableModelsAsync(binding, Token)).Single(item => item.ProviderId == "ollama");
            var observation = await rig.Bridge.SendConversationOriginalAsync(binding,
                new("Explain examples", choice.Model, ProviderId: choice.ProviderId), Token);
            await observation.WaitAsync(Token); await observation.CloseAndDrainAsync();
            Assert.Contains("blue-fern", Assert.Single(first.Requests).SystemPrompt);
            Assert.Contains("blue-fern", Assert.Single(second.Requests).SystemPrompt);
            Assert.Equal(0, remote.CatalogueCalls + remote.Requests.Count);
        }
        finally { await CloseDispatchRigAsync(rig); }
    }

    private static IReadOnlyList<Exception> DispatchLeaves(Exception failure)
    {
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>(); pending.Push(failure); var leaves = new List<Exception>();
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            if (current is AggregateException aggregate)
                foreach (var child in aggregate.InnerExceptions) pending.Push(child);
            else leaves.Add(current);
        }
        return leaves;
    }

    private static async Task CloseDispatchRigAsync(Rig rig, string? expectedRefusal = null)
    {
        try { await rig.CloseAsync(); }
        catch (Exception failure)
        {
            // Shared source custody retains repeated wrapper identities. Report each leaf
            // once instead of recursively expanding a very large aggregate graph in xUnit.
            var leaves = DispatchLeaves(failure);
            if (expectedRefusal is not null && leaves.Count > 0 && leaves.All(leaf =>
                leaf is UnauthorizedAccessException && leaf.Message.Contains(expectedRefusal, StringComparison.Ordinal))) return;
            throw new InvalidOperationException("Dispatch fixture retained at " + rig.Root + "\n" + string.Join("\n",
                leaves.Select(leaf => leaf.GetType().Name + ": " + leaf.Message + "\n" + leaf.StackTrace).Distinct()));
        }
    }

    private static AssistantConfiguration DispatchConfiguration(IReadOnlyList<string> categories) => new()
    { Name = "Local context fixture", Memory = new(true) { UcmlCategories = categories }, Model = new(AllowCloud: false, AllowFallback: false) };

    private static Rig DispatchRig(params UcmlDispatchProvider[] providers) => DispatchRigWithSafety(new Safety(), providers);

    private static Rig DispatchRigWithSafety(IConversationSafetyService safety, params UcmlDispatchProvider[] providers)
    {
        var registry = new ModelProviderRegistry(providers); var privacy = new UcmlDispatchPrivacy();
        var primary = new ProviderRoutingModelClient(new NoModelCalls(), registry, privacy);
        var router = new ResilientProviderRoutingModelClient(primary, registry, new UcmlDispatchConfigurations(providers), privacy);
        return new(configuredModelFactory: profiles => new AssistantOriginalModelSelectionOwner(registry, privacy,
            new ModelPermissionEvaluator(new UcmlDispatchModelPermissions()), profiles, primary.ToCompatibilityDescriptor),
            configuredChatFactory: (_, conversations, _) => new ChatSessionService(conversations, router,
                new CapabilityPreflightService(), safety, new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer())));
    }

    private static async Task ImportForDispatchAsync(Rig rig, AssistantConversationBinding binding, UcmlDocument document)
    {
        await using var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var view = await management.ReadAsync(binding, Token);
        var preview = await management.PrepareUcmlImportAsync(view, UcmlCodec.Serialize(document), Guid.NewGuid(), Token);
        var commit = management.CommitAsync(preview, Token);
        await DecideMemoryWrite(rig, commit, HomeApprovalChoice.Accept); Assert.True((await commit).Saved);
    }

    private static async Task DrainPrivateRequestAsync(IOllamaClient client, OllamaChatRequest request)
    { await foreach (var _ in client.StreamChatAsync(request, Token)) { } }

    private static async Task<string> DecideUcmlAskAsync(Rig rig, Task actual, HomeApprovalChoice choice,
        Action<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> beforeDecision)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < until)
        {
            var requests = (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests
                .Where(value => value.Scope.ActionName == HomeUcmlContextApprovalOwner.Action).ToArray();
            if (requests.Length > 0)
            {
                var request = Assert.Single(requests); Assert.True(request.Policy.RequiresPerActionApproval);
                beforeDecision(request);
                Assert.True((await rig.Permissions.DecideAsync(request.RequestId, choice, cancellationToken: Token)).Succeeded);
                return request.RequestId;
            }
            if (actual.IsCompleted) { await actual; throw new InvalidOperationException("UCML disclosure completed without its actual Home Ask review."); }
            await Task.Delay(20, Token);
        }
        throw new TimeoutException("The actual Home UCML Ask review was not observed.");
    }

    private sealed class UcmlDispatchPrivacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken token) { Current = preferences; return Task.CompletedTask; }
    }
    private sealed class UcmlDispatchModelPermissions : IModelPermissionStore
    {
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(ModelPermissionPolicy.Empty);
        public Task SavePolicyAsync(ModelPermissionPolicy policy, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class UcmlDispatchSafety(Action beforeStream) : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) =>
            Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) => throw new NotSupportedException();
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token)
        { if (operation == "chat.model-stream") beforeStream(); return Task.CompletedTask; }
    }
    private sealed class UcmlDispatchConfigurations(IReadOnlyList<UcmlDispatchProvider> providers) : IProviderConfigurationStore
    {
        private readonly IReadOnlyList<ProviderConfiguration> _values = providers.Select(provider => new ProviderConfiguration(
            provider.Id, provider.Kind, provider.Id, "http://127.0.0.1:11434", true, provider.IsLocal, true,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow)).ToArray();
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult(_values);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(_values.SingleOrDefault(value => value.Id == id));
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class UcmlDispatchProvider(string id, bool local) : IOriginalPrivateContextModelProvider
    {
        public List<OllamaChatRequest> Requests { get; } = [];
        public int CatalogueCalls;
        public int CompletionCalls;
        public Func<Task>? BeforeCatalogue;
        public Func<CancellationToken, Task>? BeforeStream;
        public bool FailStream;
        public string Id => id; public string DisplayName => id;
        public ModelProviderKind Kind => id == "ollama" ? ModelProviderKind.Ollama : ModelProviderKind.OpenAICompatible;
        public bool IsLocal => local; public bool CanManageModels => false;
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        {
            CatalogueCalls++;
            if (BeforeCatalogue is not null) await BeforeCatalogue();
            return [new(id, local,
                new("model", 1, "test", "1B", "fixture", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming }, DateTimeOffset.UtcNow))];
        }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => throw new InvalidOperationException("Constrained route needs no health probe.");
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            Requests.Add(request); await Task.Yield(); token.ThrowIfCancellationRequested();
            if (BeforeStream is not null) await BeforeStream(token);
            if (FailStream) throw new HttpRequestException("Actual selected fixture route failed", null, System.Net.HttpStatusCode.ServiceUnavailable);
            yield return "Local fixture response";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { Interlocked.Increment(ref CompletionCalls); throw new InvalidOperationException("No completion requested."); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("No tools authorized.");
    }
}
