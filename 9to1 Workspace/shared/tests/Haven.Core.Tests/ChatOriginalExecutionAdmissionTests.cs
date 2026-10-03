using System.Collections.Frozen;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Actual Chat loop/consumer facts with a clearly synthetic registered issuer/provider.
/// These tests do not substitute for the real Home Den/session/run authorization integration.</summary>
public sealed class ChatOriginalExecutionAdmissionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chat-original-admission", Guid.NewGuid().ToString("N"));
    public ChatOriginalExecutionAdmissionTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private static ModelDescriptor Model(string name = "original-model") => new(name, 1, "synthetic", "synthetic", "synthetic",
        new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
    private static Conversation Conversation() { var now = DateTimeOffset.UtcNow; return new(Guid.NewGuid(), HavenMode.Studio,
        ConversationKind.StudioChat, "Synthetic admission", null, null, false, true, now, now); }
    private ChatSessionService Service(Provider provider, SyntheticIssuer? issuer, TestWorkspaceTools? tools = null,
        PermitSafety? safety = null, CheckpointService? checkpoints = null) => new(new FakeConversations(), provider,
        new CapabilityPreflightService(), safety ?? new PermitSafety(), new WorkspaceToolRuntime(tools ?? new TestWorkspaceTools()),
        new ComputerToolRuntime(new TestComputerTools()), checkpoints: checkpoints, originalExecutionAdmissions: issuer);
    private async Task ReadAsync(ChatSessionService service, Conversation conversation, ModelDescriptor model, object? authority,
        CancellationToken cancellationToken)
    {
        await foreach (var _ in service.SendAsync(conversation, "Create the file", model, EffortLevel.Medium, [], "Canonical", "",
            DuoMode.Solo, _root, "", "", null, cancellationToken, originalExecutionAuthority: authority)) { }
    }

    [Fact]
    public async Task Missing_or_forged_issuer_token_refuses_before_provider_discovery_or_context_work()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(Service(provider, null), conversation, model, new object(), lifetime.Token));
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 2);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(Service(provider, issuer), conversation, model, new object(), lifetime.Token));
        Assert.Equal(0, provider.Discoveries); Assert.Equal(0, provider.ModelRequests); Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Zero_original_tool_budget_refuses_the_actual_callback_before_any_write()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model);
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 0);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(Service(provider, issuer), conversation, model, issuer.Token, timeout.Token));
        Assert.Equal(1, provider.ModelRequests); Assert.Equal(0, issuer.AdmittedCalls);
        Assert.Empty(Directory.GetFiles(_root)); Assert.Same(provider.Calls[0], issuer.LastDemandedCall);
    }

    [Fact]
    public async Task Cumulative_actual_callbacks_consume_original_budget_and_second_dispatch_cannot_write()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model) { TwoCalls = true };
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 1);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(Service(provider, issuer), conversation, model, issuer.Token, timeout.Token));
        Assert.Equal(1, issuer.AdmittedCalls); Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_root, "first.txt"), timeout.Token));
        Assert.False(File.Exists(Path.Combine(_root, "second.txt"))); Assert.Same(provider.Calls[1], issuer.LastDemandedCall);
    }

    [Fact]
    public async Task Revocation_after_original_model_result_refuses_before_the_actual_tool_callback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 2);
        var provider = new Provider(model) { AfterModel = () => issuer.Current = false };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(Service(provider, issuer), conversation, model, issuer.Token, timeout.Token));
        Assert.Equal(1, provider.ModelRequests); Assert.Equal(0, issuer.AdmittedCalls); Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Actual_fallback_model_identity_requires_fresh_original_admission()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var requested = new ModelDescriptor("original-model", 1, "synthetic", "synthetic", "synthetic",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow); var actual = Model("different-tools-model"); var conversation = Conversation();
        using var issuer = new SyntheticIssuer(conversation.Id, requested.Name, 2);
        var provider = new Provider(actual);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(Service(provider, issuer), conversation, requested, issuer.Token, timeout.Token));
        Assert.Equal(1, provider.Discoveries); Assert.Equal(0, provider.ModelRequests); Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Original_issuer_lifetime_cancels_the_same_inflight_model_request_even_with_independent_caller_token()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 2);
        var provider = new Provider(model) { PauseModel = true };
        var pending = ReadAsync(Service(provider, issuer), conversation, model, issuer.Token, timeout.Token);
        Exception? primary = null; OperationCanceledException? observedCancellation = null;
        try
        {
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
            issuer.Lifetime.Cancel();
            observedCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(provider.OriginalModelSettled); Assert.Empty(Directory.GetFiles(_root));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Exception? cancel = null;
            try { issuer.Lifetime.Cancel(); } catch (Exception error) { cancel = error; }
            try { await pending.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception error) when (ReferenceEquals(error, observedCancellation)) { }
            catch (OperationCanceledException error) when (error.CancellationToken == provider.OriginalModelToken &&
                issuer.Lifetime.IsCancellationRequested && provider.OriginalModelToken.IsCancellationRequested && cancel is null) { }
            catch (Exception error) when (ReferenceEquals(error, primary)) { }
            catch (Exception error) { throw new AggregateException(new[] { primary, cancel, error }.OfType<Exception>()); }
            if (cancel is not null) throw new AggregateException(new[] { primary, cancel }.OfType<Exception>());
        }
    }

    [Fact]
    public async Task Legacy_local_null_authority_path_preserves_actual_tool_loop_behavior()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model);
        await ReadAsync(Service(provider, null), conversation, model, null, timeout.Token);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_root, "first.txt"), timeout.Token));
        Assert.Equal(2, provider.ModelRequests);
    }

    [Fact]
    public async Task Original_tool_result_revocation_preserves_actual_write_but_refuses_result_publication()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 1);
        var tools = new TestWorkspaceTools { AfterWrite = () => issuer.Current = false };
        var provider = new Provider(model); var events = new List<ChatStreamEvent>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await foreach (var item in Service(provider, issuer, tools).SendAsync(conversation, "Create the file", model,
                EffortLevel.Medium, [], "Canonical", "", DuoMode.Solo, _root, "", "", null, timeout.Token,
                originalExecutionAuthority: issuer.Token)) events.Add(item);
        });
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_root, "first.txt"), timeout.Token));
        Assert.Equal(1, issuer.AdmittedCalls); Assert.Equal(1, provider.ModelRequests);
        Assert.DoesNotContain(events, item => item.Kind is ChatStreamEventKind.ToolActivity or
            ChatStreamEventKind.AssistantDelta or ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Awaited_original_checkpoint_revocation_refuses_dispatch_without_spending_a_second_call()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 1);
        var repository = new CheckpointRepository(() => issuer.Current = false);
        var checkpoints = new CheckpointService(repository, new UnusedRestorer());
        var provider = new Provider(model);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ReadAsync(Service(provider, issuer, checkpoints: checkpoints), conversation, model, issuer.Token, timeout.Token));
        Assert.Equal(1, repository.Saves); Assert.Equal(1, issuer.AdmittedCalls);
        Assert.Equal(1, provider.ModelRequests); Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Deterministic_GenUI_publication_requires_current_original_admission()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 0);
        var provider = new Provider(model);
        var original = Service(provider, issuer).SendAsync(conversation, "Is generative UI available?", model,
            EffortLevel.Medium, [], "Canonical", "", DuoMode.Solo, _root, "", "", null, timeout.Token,
            originalExecutionAuthority: issuer.Token).GetAsyncEnumerator(timeout.Token);
        Exception? primary = null; UnauthorizedAccessException? refused = null;
        try
        {
            Assert.True(await original.MoveNextAsync()); Assert.Equal(ChatStreamEventKind.UserMessage, original.Current.Kind);
            issuer.Current = false;
            refused = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await original.MoveNextAsync());
            Assert.Equal(0, provider.Discoveries); Assert.Equal(0, provider.ModelRequests); Assert.Equal(0, issuer.AdmittedCalls);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { await original.DisposeAsync(); }
            catch (Exception error) when (ReferenceEquals(error, refused) || ReferenceEquals(error, primary)) { }
            catch (Exception error) { throw primary is null ? error : new AggregateException(primary, error); }
        }
    }

    [Fact]
    public async Task Stream_publication_rechecks_after_the_actual_awaited_safety_boundary()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 0);
        var safety = new PermitSafety(operation => { if (operation == "chat.model-stream-chunk") issuer.Current = false; });
        var provider = new Provider(model); var events = new List<ChatStreamEvent>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await foreach (var item in Service(provider, issuer, safety: safety).SendAsync(conversation, "Describe this",
                model, EffortLevel.Medium, [], "Canonical", "", DuoMode.Solo, null, "", "", null, timeout.Token,
                explicitCapabilities: [ToolCapability.Text], originalExecutionAuthority: issuer.Token)) events.Add(item);
        });
        Assert.DoesNotContain(events, item => item.Kind is ChatStreamEventKind.AssistantDelta or ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(0, issuer.AdmittedCalls);
    }

    [Fact]
    public async Task Actual_main_model_failure_and_original_timer_event_failure_both_survive_consumer_cleanup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var model = Model(); var conversation = Conversation(); using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 0);
        var mainFailure = new IOException("synthetic original model refusal");
        var timerFailure = new InvalidOperationException("synthetic original timer event refusal");
        var timerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider(model) { ModelFailure = mainFailure };
        var service = Service(provider, issuer);
        service.ExecutionChanged += snapshot =>
        {
            if (!snapshot.IsVisible || snapshot.Stage == ChatExecutionStage.Cancelled) return;
            timerEntered.TrySetResult(); throw timerFailure;
        };
        var pending = ReadAsync(service, conversation, model, issuer.Token, timeout.Token);
        Exception? primary = null; AggregateException? expected = null;
        try
        {
            await provider.Entered.Task.WaitAsync(timeout.Token);
            await timerEntered.Task.WaitAsync(timeout.Token); // The maintained real two-second visibility timer.
            provider.ReleaseModelFailure.TrySetResult();
            expected = await Assert.ThrowsAsync<AggregateException>(() => pending);
            var actual = expected.Flatten().InnerExceptions;
            Assert.Contains(actual, item => ReferenceEquals(item, mainFailure));
            Assert.Contains(actual, item => ReferenceEquals(item, timerFailure));
            Assert.True(provider.OriginalModelSettled); Assert.Empty(Directory.GetFiles(_root));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            provider.ReleaseModelFailure.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception error) when (ReferenceEquals(error, expected) || ReferenceEquals(error, primary)) { }
            catch (Exception error) { throw primary is null ? error : new AggregateException(primary, error); }
        }
    }

    [Fact]
    public async Task Same_actual_call_and_result_settle_before_the_next_original_callback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model) { TwoCalls = true };
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 2);
        await ReadAsync(Service(provider, issuer), conversation, model, issuer.Token, timeout.Token);
        Assert.Equal(2, issuer.Settlements.Count); Assert.Equal(2, issuer.AdmittedCalls);
        Assert.Same(provider.Calls[0], issuer.Settlements[0].Call); Assert.Same(provider.Calls[1], issuer.Settlements[1].Call);
        Assert.NotNull(issuer.Settlements[0].Result); Assert.NotNull(issuer.Settlements[1].Result);
        Assert.Null(issuer.Settlements[0].Failure); Assert.Null(issuer.Settlements[1].Failure);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_root, "first.txt"), timeout.Token));
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(_root, "second.txt"), timeout.Token));
    }

    [Fact]
    public async Task Unconfirmed_original_settlement_retains_the_write_and_refuses_second_tool_or_publication()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model) { TwoCalls = true };
        var refusal = new UnauthorizedAccessException("Synthetic owner receipt unavailable.");
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 2) { SettlementFailure = refusal };
        var events = new List<ChatStreamEvent>();
        var actual = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            await foreach (var item in Service(provider, issuer).SendAsync(conversation, "Create the file", model,
                EffortLevel.Medium, [], "Canonical", "", DuoMode.Solo, _root, "", "", null, timeout.Token,
                originalExecutionAuthority: issuer.Token)) events.Add(item);
        });
        Assert.Same(refusal, actual); Assert.Single(issuer.Settlements); Assert.Same(provider.Calls[0], issuer.Settlements[0].Call);
        Assert.NotNull(issuer.Settlements[0].Result); Assert.Equal(1, issuer.AdmittedCalls);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_root, "first.txt"), timeout.Token));
        Assert.False(File.Exists(Path.Combine(_root, "second.txt"))); Assert.Equal(1, provider.ModelRequests);
        Assert.DoesNotContain(events, item => item.Kind is ChatStreamEventKind.ToolActivity or ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Original_tool_exception_and_distinct_settlement_failure_both_survive_the_actual_loop()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model);
        var original = new OperationCanceledException("Synthetic unrelated original runtime refusal.", CancellationToken.None);
        var settlement = new IOException("Synthetic owner receipt recording failure.");
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 1) { SettlementFailure = settlement };
        var tools = new TestWorkspaceTools { AfterWrite = () => throw original };
        var actual = await Assert.ThrowsAsync<AggregateException>(() =>
            ReadAsync(Service(provider, issuer, tools), conversation, model, issuer.Token, timeout.Token));
        Assert.Contains(actual.InnerExceptions, error => ReferenceEquals(error, original));
        Assert.Contains(actual.InnerExceptions, error => ReferenceEquals(error, settlement));
        Assert.Single(issuer.Settlements); Assert.Same(provider.Calls[0], issuer.Settlements[0].Call);
        Assert.Same(original, issuer.Settlements[0].Failure); Assert.Null(issuer.Settlements[0].Result);
        Assert.Equal(1, issuer.AdmittedCalls); Assert.Equal(1, provider.ModelRequests);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_root, "first.txt"), timeout.Token));
    }

    [Fact]
    public async Task Original_lifetime_retirement_after_actual_write_still_forwards_same_outcome_and_refuses_more_work()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model) { TwoCalls = true };
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 2);
        var tools = new TestWorkspaceTools { AfterWrite = () => issuer.Lifetime.Cancel() };
        var events = new List<ChatStreamEvent>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in Service(provider, issuer, tools).SendAsync(conversation, "Create the file", model,
                EffortLevel.Medium, [], "Canonical", "", DuoMode.Solo, _root, "", "", null, timeout.Token,
                originalExecutionAuthority: issuer.Token)) events.Add(item);
        });
        Assert.True(issuer.Lifetime.IsCancellationRequested);
        var settled = Assert.Single(issuer.Settlements);
        Assert.Same(provider.Calls[0], settled.Call); Assert.NotNull(settled.Result); Assert.Null(settled.Failure);
        Assert.Equal(CancellationToken.None, Assert.Single(issuer.SettlementTokens));
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_root, "first.txt"), timeout.Token));
        Assert.False(File.Exists(Path.Combine(_root, "second.txt")));
        Assert.Equal(1, issuer.AdmittedCalls); Assert.Equal(1, provider.ModelRequests);
        Assert.DoesNotContain(events, item => item.Kind is ChatStreamEventKind.ToolActivity or ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Original_argument_mutation_after_awaited_checkpoint_refuses_dispatch_and_retains_frozen_body()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var model = Model(); var conversation = Conversation(); var provider = new Provider(model);
        using var issuer = new SyntheticIssuer(conversation.Id, model.Name, 1);
        var originalArguments = Assert.IsType<Dictionary<string, JsonElement>>(provider.Calls[0].Arguments);
        var repository = new CheckpointRepository(() => originalArguments["path"] = JsonSerializer.SerializeToElement("altered.txt"));
        var checkpoints = new CheckpointService(repository, new UnusedRestorer());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ReadAsync(Service(provider, issuer, checkpoints: checkpoints), conversation, model, issuer.Token, timeout.Token));
        var dispatch = Assert.IsType<OllamaToolCall>(issuer.LastDispatchCall);
        Assert.NotSame(provider.Calls[0], dispatch);
        Assert.IsAssignableFrom<FrozenDictionary<string, JsonElement>>(dispatch.Arguments);
        Assert.Equal("first.txt", dispatch.Arguments["path"].GetString());
        Assert.Equal("altered.txt", originalArguments["path"].GetString());
        Assert.Equal(1, repository.Saves); Assert.Equal(1, issuer.AdmittedCalls);
        Assert.Equal(1, provider.ModelRequests); Assert.Empty(Directory.GetFiles(_root));
        Assert.Same(provider.Calls[0], Assert.Single(issuer.Settlements).Call);
    }

    private sealed class SyntheticIssuer(Guid conversationId, string modelIdentity, long maximumCalls) : IChatExecutionAdmission, IDisposable
    {
        public object Token { get; } = new();
        public CancellationTokenSource Lifetime { get; } = new(TimeSpan.FromSeconds(60));
        public bool Current { get; set; } = true;
        public long AdmittedCalls { get; private set; }
        private OllamaToolCall? _admittedCall;
        private OllamaToolCall? _dispatchCall;
        private string? _callBody;
        public OllamaToolCall? LastDispatchCall { get; private set; }
        public Exception? SettlementFailure { get; init; }
        public List<(OllamaToolCall Call, WorkspaceToolResult? Result, Exception? Failure)> Settlements { get; } = [];
        public List<CancellationToken> SettlementTokens { get; } = [];
        public OllamaToolCall? LastDemandedCall { get; private set; }
        public ValueTask DemandCurrentAsync(object authority, Guid actualConversation, string actualModel,
            OllamaToolCall? call, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Token, authority) || !Current || actualConversation != conversationId || actualModel != modelIdentity)
                throw new UnauthorizedAccessException("Synthetic original admission refused.");
            if (_admittedCall is not null && Snapshot(_admittedCall) != _callBody)
                throw new UnauthorizedAccessException("Synthetic original arguments changed.");
            if (call is not null)
            {
                LastDemandedCall = call;
                if (AdmittedCalls >= maximumCalls) throw new UnauthorizedAccessException("Synthetic original tool budget refused.");
                if (_admittedCall is not null) throw new UnauthorizedAccessException("Synthetic prior call remains unconfirmed.");
                AdmittedCalls++; _admittedCall = call; _callBody = Snapshot(call);
                _dispatchCall = call with { Arguments = call.Arguments.ToFrozenDictionary(item => item.Key,
                    item => item.Value.Clone(), StringComparer.Ordinal) };
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask<CancellationToken> GetOriginalLifetimeAsync(object authority, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Token, authority) || !Current) throw new UnauthorizedAccessException("Synthetic lifetime refused.");
            return ValueTask.FromResult(Lifetime.Token);
        }
        public ValueTask<OllamaToolCall> GetOriginalDispatchCallAsync(object authority, Guid actualConversation,
            string actualModel, OllamaToolCall call, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Token, authority) || !Current || actualConversation != conversationId || actualModel != modelIdentity ||
                !ReferenceEquals(call, _admittedCall) || Snapshot(call) != _callBody || _dispatchCall is null)
                throw new UnauthorizedAccessException("Synthetic immutable dispatch refused.");
            LastDispatchCall = _dispatchCall; return ValueTask.FromResult(_dispatchCall);
        }
        private static string Snapshot(OllamaToolCall call) => JsonSerializer.Serialize(new { call.Name, call.Id,
            Arguments = call.Arguments.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new { item.Key, Value = item.Value.GetRawText() }).ToArray() });
        public ValueTask CompleteOriginalCallAsync(object authority, Guid actualConversation, string actualModel,
            OllamaToolCall call, OllamaToolCall dispatchCall, WorkspaceToolResult? result, Exception? failure, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Token, authority) || actualConversation != conversationId || actualModel != modelIdentity ||
                !ReferenceEquals(call, _admittedCall) || !ReferenceEquals(dispatchCall, _dispatchCall) || (result is null) == (failure is null))
                throw new UnauthorizedAccessException("Synthetic exact original outcome refused.");
            // This clearly synthetic recording seam supplies no production receipt/authority.
            Settlements.Add((call, result, failure)); SettlementTokens.Add(ct);
            if (SettlementFailure is { } refused) throw refused;
            _admittedCall = null; _dispatchCall = null; _callBody = null; return ValueTask.CompletedTask;
        }
        public void Dispose() => Lifetime.Dispose();
    }

    private sealed class Provider(ModelDescriptor model) : IOllamaClient
    {
        public int Discoveries { get; private set; }
        public int ModelRequests { get; private set; }
        public bool TwoCalls { get; init; }
        public bool PauseModel { get; init; }
        public Exception? ModelFailure { get; init; }
        public TaskCompletionSource ReleaseModelFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OriginalModelSettled { get; private set; }
        public CancellationToken OriginalModelToken { get; private set; }
        public Action? AfterModel { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<OllamaToolCall> Calls { get; } = [Call("first.txt", "first"), Call("second.txt", "second")];
        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Discoveries++; return Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]); }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(""); }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; ct.ThrowIfCancellationRequested(); yield return "Synthetic text"; }
        public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct)
        {
            ModelRequests++; OriginalModelToken = ct;
            if (ModelFailure is { } originalFailure)
            {
                Entered.TrySetResult();
                try { await ReleaseModelFailure.Task.WaitAsync(ct); throw originalFailure; }
                finally { OriginalModelSettled = true; }
            }
            if (PauseModel)
            {
                Entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { OriginalModelSettled = true; }
            }
            ct.ThrowIfCancellationRequested(); AfterModel?.Invoke();
            return ModelRequests == 1 ? new("", TwoCalls ? Calls : [Calls[0]]) : new("Synthetic completed response", []);
        }
        private static OllamaToolCall Call(string path, string content)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { path, content }));
            return new("write_file", json.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone()));
        }
    }
    private sealed class PermitSafety(Action<string>? afterSafety = null) : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken ct) => Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken ct) => Task.FromResult(new ConversationSafetyFlagResult(true, false, new ConversationSafetySnapshot(id, 1, ConversationSafetyState.Active, null, 1)));
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (afterSafety is null) return Task.CompletedTask;
            return ObserveOriginalSafetyAsync();
            async Task ObserveOriginalSafetyAsync()
            { await Task.Yield(); afterSafety(operation); ct.ThrowIfCancellationRequested(); }
        }
    }
    private sealed class FakeConversations : IConversationRepository
    {
        private readonly bool _recordMessages;
        public FakeConversations(bool recordMessages = false) => _recordMessages = recordMessages;
        public List<ChatMessage> Messages { get; } = [];
        /// <summary>
        /// Retrieves recent async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        /// <summary>
        /// Retrieves messages async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>(_recordMessages ? Messages.Where(message => message.ConversationId == conversationId).ToArray() : []);
        /// <summary>
        /// Performs upsert conversation asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs add message asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            if (_recordMessages) Messages.Add(message);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs delete conversation asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CheckpointRepository(Action afterSave) : ICheckpointRepository
    {
        public int Saves { get; private set; }
        public async Task SaveAsync(CheckpointInfo checkpoint, CancellationToken token)
        { token.ThrowIfCancellationRequested(); await Task.Yield(); Saves++; afterSave(); }
        public Task<CheckpointInfo?> GetLatestAsync(Guid? id, string root, CancellationToken token) => Task.FromResult<CheckpointInfo?>(null);
        public Task<CheckpointInfo?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<CheckpointInfo?>(null);
        public Task<long> GetLatestVersionSequenceAsync(string root, CancellationToken token) => Task.FromResult(0L);
        public Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string root, long sequence, CancellationToken token) => Task.FromResult<IReadOnlyList<WorkspaceRestoreEntry>>([]);
        public Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string root, CancellationToken token) => Task.FromResult<WorkspaceRestoreEntry?>(null);
    }
    private sealed class UnusedRestorer : ICheckpointRestorer
    {
        public Task<IReadOnlyList<string>> RestoreAsync(string root, CheckpointRestorePlan plan, CancellationToken token) => throw new InvalidOperationException("The original checkpoint test never restores.");
    }

    /// <summary>
    /// Represents test workspace tools and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestWorkspaceTools : IWorkspaceToolService
    {
        public Action? AfterWrite { get; init; }
        /// <summary>
        /// Performs the resolve workspace path step owned by this component.
        /// </summary>
        public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
        {
            var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var result = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("outside workspace");
            return result;
        }

        /// <summary>
        /// Performs read text asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) => File.ReadAllTextAsync(ResolveWorkspacePath(workspaceRoot, relativePath), cancellationToken);
        /// <summary>
        /// Performs write text atomic asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
        {
            var path = ResolveWorkspacePath(workspaceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
            AfterWrite?.Invoke();
        }
        /// <summary>
        /// Performs search files asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        /// <summary>
        /// Runs run process async while preserving the surrounding cancellation and error-handling contract.
        /// </summary>
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// Represents test computer tools and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestComputerTools : IComputerToolService
    {
        public bool IsSupported => true;
        public ValueTask<bool> VerifyTargetAsync(string canonicalAppId, string toolName, System.Text.Json.JsonElement arguments,
            CancellationToken cancellationToken) => ValueTask.FromResult(canonicalAppId == "fixture.native-app");
        /// <summary>
        /// Gets or updates launched name, the bindable or domain state represented by this property.
        /// </summary>
        public string? LaunchedName { get; private set; }
        /// <summary>
        /// Performs snapshot asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult("snapshot");
        /// <summary>
        /// Performs list windows asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ListWindowsAsync(CancellationToken cancellationToken) => Task.FromResult("[]");
        /// <summary>
        /// Performs launch app asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken) { LaunchedName = name; return Task.FromResult($"opened {name}"); }
        /// <summary>
        /// Performs focus window asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult($"focused {title}");
        /// <summary>
        /// Performs invoke asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken) => Task.FromResult("invoked");
        /// <summary>
        /// Performs click asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken) => Task.FromResult("clicked");
        /// <summary>
        /// Performs type asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken) => Task.FromResult("typed");
        /// <summary>
        /// Performs press asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken) => Task.FromResult("pressed");
        /// <summary>
        /// Performs close window asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult("closed");
    }
}
