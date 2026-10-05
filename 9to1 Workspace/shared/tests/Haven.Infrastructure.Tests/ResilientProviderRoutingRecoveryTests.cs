using System.Net;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>Original production router regressions; synthetic providers do not certify a deployed model.</summary>
public sealed class ResilientProviderRoutingRecoveryTests
{
    [Fact]
    public void OriginalSixArgumentClrConstructorRemainsAvailableToCompiledOrdinaryClients()
    {
        var original = Assert.IsAssignableFrom<System.Reflection.ConstructorInfo>(
            typeof(ResilientProviderRoutingModelClient).GetConstructor(
                [typeof(ProviderRoutingModelClient), typeof(IModelProviderRegistry),
                 typeof(IProviderConfigurationStore), typeof(IPrivacyPreferenceStore),
                 typeof(IModelFallbackOrderStore), typeof(IExecutionEventSink)]));
        Assert.All(original.GetParameters(), parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public async Task ProgrammingOrAdmissionInvalidOperationDoesNotTriggerFallback()
    {
        var denied = new InvalidOperationException("Original admission refused");
        var first = new Provider("first", completion: _ => Task.FromException<string>(denied));
        var second = new Provider("second");
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => Client([first, second]).CompleteAsync(Chat(first), default));
        Assert.Same(denied, observed);
        Assert.Equal(0, second.CompletionCalls);
    }

    [Fact]
    public async Task CallerCancellationIsPreservedWithoutFallback()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var first = new Provider("first");
        var second = new Provider("second");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client([first, second]).CompleteAsync(Chat(first), cancellation.Token));
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(0, second.CompletionCalls);
    }

    [Fact]
    public async Task ExplicitProviderTimeoutRecoversWithoutCallerCancellation()
    {
        var timeout = new TaskCanceledException("Provider request timed out", new TimeoutException("Original provider timeout"));
        var first = new Provider("first", completion: _ => Task.FromException<string>(timeout));
        var second = new Provider("second");
        Assert.Equal("second result", await Client([first, second]).CompleteAsync(Chat(first), default));
        Assert.Equal(1, first.CompletionCalls);
        Assert.Equal(1, second.CompletionCalls);
    }

    [Fact]
    public async Task UntypedInternalCancellationDoesNotTriggerFallback()
    {
        var stopped = new TaskCanceledException("Original worker was stopped");
        var first = new Provider("first", completion: _ => Task.FromException<string>(stopped));
        var second = new Provider("second");
        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client([first, second]).CompleteAsync(Chat(first), default));
        Assert.Same(stopped, observed);
        Assert.Equal(0, second.CompletionCalls);
    }

    [Fact]
    public async Task MultipleDirectCompletionCausesRemainOriginalAndRefuseFallback()
    {
        var quota = Quota();
        var sibling = new UnauthorizedAccessException("Original sibling denial");
        var actual = new TaskCompletionSource<string>();
        actual.SetException([quota, sibling]);
        var first = new Provider("first", completion: _ => actual.Task);
        var second = new Provider("second");
        var observed = await Assert.ThrowsAsync<AggregateException>(() => Client([first, second]).CompleteAsync(Chat(first), default));
        Assert.True(ContainsOriginal(observed, quota));
        Assert.True(ContainsOriginal(observed, sibling));
        Assert.Equal(0, second.CompletionCalls);
        Assert.Equal(2, actual.Task.Exception!.InnerExceptions.Count);
    }

    [Fact]
    public async Task MultipleDirectToolCausesRemainOriginalAndRefuseFallback()
    {
        var quota = Quota();
        var sibling = new InvalidOperationException("Original second tool-provider cause");
        var actual = new TaskCompletionSource<OllamaToolResponse>();
        actual.SetException([quota, sibling]);
        var first = new Provider("first", tools: _ => actual.Task);
        var second = new Provider("second");
        var observed = await Assert.ThrowsAsync<AggregateException>(() => Client([first, second]).ChatWithToolsAsync(Tools(first), default));
        Assert.True(ContainsOriginal(observed, quota));
        Assert.True(ContainsOriginal(observed, sibling));
        Assert.Equal(0, second.ToolCalls);
    }

    [Fact]
    public async Task StandaloneFallbackDoesNotInventAnExecutionOrActionGraphRun()
    {
        var sink = new Sink();
        var first = new Provider("first", completion: _ => Task.FromException<string>(Quota()));
        var second = new Provider("second");
        Assert.Equal("second result", await Client([first, second], sink).CompleteAsync(Chat(first), default));
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task NonOllamaLocalFallbackKeepsItsActualRegisteredProviderKey()
    {
        var first = new Provider("first", isLocal: true, completion: _ => Task.FromException<string>(Quota()));
        var second = new Provider("second", isLocal: true);
        Assert.Equal("second result", await Client([first, second]).CompleteAsync(Chat(first), default));
        Assert.Equal(1, first.CompletionCalls);
        Assert.Equal(1, second.CompletionCalls);
        Assert.Equal(second.Descriptor.Name, second.LastChatRequest!.Model);
    }

    [Fact]
    public async Task ToolResponseReportsActualFallbackDescriptorAndKeepsOriginalTranscript()
    {
        var first = new Provider("first", tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        var second = new Provider("second");
        var request = Tools(first);
        var result = await Client([first, second]).ChatWithToolsAsync(request, default);
        Assert.Same(second.Descriptor, result.EffectiveModel);
        Assert.Same(request.Messages, second.LastToolRequest!.Messages);
        Assert.Same(request.Tools, second.LastToolRequest.Tools);
        Assert.Equal(second.Descriptor.Name, second.LastToolRequest.Model);
    }

    [Fact]
    public async Task MalformedToolResponseIsNotARecoverableProviderFailure()
    {
        var first = new Provider("first", tools: _ => Task.FromResult<OllamaToolResponse>(null!));
        var second = new Provider("second");
        await Assert.ThrowsAsync<InvalidDataException>(() => Client([first, second]).ChatWithToolsAsync(Tools(first), default));
        Assert.Equal(0, second.ToolCalls);
    }

    [Fact]
    public async Task RestrictedToolFallbackSkipsGovernanceDeniedCandidateUsingActualModelPolicy()
    {
        var first = new Provider("first", tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        var denied = new Provider("second");
        var eligible = new Provider("third");
        var policy = new ModelPermissionPolicy([ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel,
            denied.Descriptor.Key, ModelPermissionScope.ThisDevice, RestrictedModelCapability.RunCommands)]);
        var evaluator = new ModelPermissionEvaluator(new PermissionStore(policy));
        var request = Tools(first) with { Tools = [new("run_command", "Synthetic offered restricted tool", new Dictionary<string, object>(), [])] };
        var response = await Client([first, denied, eligible], permissions: evaluator).ChatWithToolsAsync(request, default);
        Assert.Same(eligible.Descriptor, response.EffectiveModel);
        Assert.Equal(1, first.ToolCalls);
        Assert.Equal(0, denied.ToolCalls);
        Assert.Equal(1, eligible.ToolCalls);
        Assert.Same(request.Tools, eligible.LastToolRequest!.Tools);
    }

    [Fact]
    public async Task RequestedModelGovernanceDenialCannotSilentlySelectAnotherProvider()
    {
        var first = new Provider("first");
        var second = new Provider("second");
        var policy = new ModelPermissionPolicy([ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel,
            first.Descriptor.Key, ModelPermissionScope.ThisDevice, RestrictedModelCapability.RunCommands)]);
        var evaluator = new ModelPermissionEvaluator(new PermissionStore(policy));
        var request = Tools(first) with { Tools = [new("run_command", "Synthetic offered restricted tool", new Dictionary<string, object>(), [])] };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Client([first, second], permissions: evaluator).ChatWithToolsAsync(request, default));
        Assert.Equal(0, first.ToolCalls);
        Assert.Equal(0, second.ToolCalls);
    }

    [Fact]
    public async Task OriginalStreamBodyAndDisposeFailuresAreBothRetained()
    {
        var body = Quota();
        var cleanup = new IOException("Original stream cleanup failed");
        var first = new Provider("first", stream: _ => new FailedStream(body, cleanup));
        var second = new Provider("second");
        var observed = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            await foreach (var _ in Client([first, second]).StreamChatAsync(Chat(first), default)) { }
        });
        Assert.True(ContainsOriginal(observed, body));
        Assert.True(ContainsOriginal(observed, cleanup));
        Assert.Equal(0, second.StreamCalls);
    }

    [Fact]
    public async Task ThrowingCancellationCallbackCannotSkipEarlyDisposeOriginalCleanup()
    {
        var callback = new InvalidOperationException("Original cancellation callback fault");
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Provider("first", stream: token => CancelFaultStream(token, callback, cleanup));
        var second = new Provider("second");
        var original = Client([first, second]).StreamChatAsync(Chat(first), default).GetAsyncEnumerator();
        Assert.True(await original.MoveNextAsync());
        Assert.Equal("first chunk", original.Current);
        var observed = await Assert.ThrowsAnyAsync<Exception>(() => original.DisposeAsync().AsTask());
        await cleanup.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(ContainsOriginal(observed, callback));
        Assert.True(cleanup.Task.IsCompletedSuccessfully);
        Assert.Equal(0, second.StreamCalls);
    }

    [Fact]
    public async Task QuotaAfterAcceptedToolStateResumesSameTaskRunCheckpointAndHealthyAttempt()
    {
        var quota = Quota();
        var first = new Provider("first", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(quota));
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        await fixture.Coordinator.SubmitFollowUpAsync(fixture.Task.TaskId, "afterwards preserve queued work", TaskFollowUpMode.Queue, [], [], default);
        var before = await fixture.CurrentAsync();
        var accepted = before.Plan.Single();
        IReadOnlyList<OllamaToolTurn> transcript = [new("user", "Continue original task"),
            new("assistant", "Accepted operation", [new("read_file", new Dictionary<string, JsonElement>(), accepted.ActionId.ToString("D"))]),
            new("tool", "Original accepted result", ToolName: "read_file")];
        var request = Tools(first) with { Messages = transcript, ExecutionContext = fixture.Context(before) };
        var response = await fixture.Client.ChatWithToolsAsync(request, default);
        var after = await fixture.CurrentAsync();
        Assert.Equal(before.TaskId, after.TaskId);
        Assert.Equal(before.ContextId, after.ContextId);
        Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
        Assert.Equal(JsonSerializer.Serialize(accepted), JsonSerializer.Serialize(after.Plan.Single()));
        Assert.Equal(JsonSerializer.Serialize(before.Queue), JsonSerializer.Serialize(after.Queue));
        Assert.Equal(2, after.Attempts.Count);
        Assert.Equal(before.Attempts.Single().Id, after.Attempts.Last().RetryOfAttemptId);
        Assert.Equal(TaskRunAttemptState.Failed, after.Attempts.First().State);
        Assert.Equal(TaskRunAttemptState.Running, after.Attempts.Last().State);
        Assert.Same(transcript, first.LastToolRequest!.Messages);
        Assert.Same(transcript, second.LastToolRequest!.Messages);
        Assert.Same(second.Descriptor, response.EffectiveModel);
        Assert.Equal(after.ExecutionId, response.ExecutionContext!.ExecutionId);
        Assert.All(fixture.Events.Events, value => { Assert.Equal(after.ExecutionId, value.ExecutionId); Assert.Equal(after.TaskId, value.TaskId); });
        Assert.Equal(1, fixture.Authority.Leases.First().Disposals);
        // A further tool turn uses the SAME still-live new attempt instead of closing a lease per request.
        await fixture.Client.ChatWithToolsAsync(request with { Model = second.Descriptor.Key, ExecutionContext = fixture.Context(after) }, default);
        Assert.Equal(2, (await fixture.CurrentAsync()).Attempts.Count);
        Assert.Equal(2, second.ToolCalls);
        Assert.Equal(0, fixture.Authority.Leases.Last().Disposals);
        await fixture.Frames.CloseAndDrainAsync();
        Assert.All(fixture.Authority.Leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public async Task CatalogueOutageAfterActualZeroFrameRegistrationUsesRetainedSelectionAndSameRun()
    {
        var first = new Provider("first", isLocal: true);
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var before = await fixture.CurrentAsync();
        first.CatalogueFailure = new HttpRequestException("Original catalogue unavailable", null, HttpStatusCode.ServiceUnavailable);
        var result = await fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default);
        var after = await fixture.CurrentAsync();
        Assert.Equal("second result", result);
        Assert.Equal(before.TaskId, after.TaskId);
        Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
        Assert.Equal(JsonSerializer.Serialize(before.Plan.Single()), JsonSerializer.Serialize(after.Plan.Single()));
        Assert.Equal("PROVIDER_CATALOGUE_UNAVAILABLE", after.Attempts.First().Failure!.Code);
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(1, second.CompletionCalls);
        Assert.Equal(1, fixture.Authority.Leases.First().Disposals);
        await fixture.Frames.CloseAndDrainAsync();
        Assert.All(fixture.Authority.Leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public async Task ActualCanceledTimeoutFrameUsesOwnerBodyCauseToRecoverSameRun()
    {
        var timeout = new TaskCanceledException("Original provider HTTP timeout", new TimeoutException("Original timeout cause"));
        var first = new Provider("first", isLocal: true, completion: _ => Task.FromException<string>(timeout));
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var before = await fixture.CurrentAsync();
        Assert.Equal("second result", await fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default));
        var after = await fixture.CurrentAsync();
        Assert.Equal(before.TaskId, after.TaskId);
        Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
        Assert.Equal("PROVIDER_TIMEOUT", after.Attempts.First().Failure!.Code);
        Assert.Equal(2, after.Attempts.Count);
        Assert.Equal(1, first.CompletionCalls);
        Assert.Equal(1, second.CompletionCalls);
        await fixture.Frames.CloseAndDrainAsync();
        Assert.All(fixture.Authority.Leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public async Task AcknowledgedQuotaCannotWaiveAnotherOriginalToolFrameFault()
    {
        var quota = Quota();
        var sibling = new UnauthorizedAccessException("Original independent tool frame denial");
        var first = new Provider("first", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(quota));
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var siblingFrame = fixture.Frames.StartOriginalToolFrameAsync(fixture.Admission, _ => Task.FromException<bool>(sibling), default);
        Assert.Same(sibling, await Assert.ThrowsAsync<UnauthorizedAccessException>(() => siblingFrame));
        var before = await fixture.CurrentAsync();
        var observed = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(before) }, default));
        Assert.True(ContainsOriginal(observed, sibling));
        Assert.Equal(0, second.ToolCalls);
        var after = await fixture.CurrentAsync();
        Assert.Single(after.Attempts);
        Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
        var close = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Frames.CloseAndDrainAsync());
        Assert.True(ContainsOriginal(close, sibling));
        Assert.Equal(1, fixture.Authority.Leases.Single().Disposals);
    }

    [Fact]
    public async Task RejectedFailureCheckpointDoesNotAcknowledgeOrDispatchFallback()
    {
        var quota = Quota();
        var refused = new IOException("Original fixture failure CAS refused");
        var first = new Provider("first", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(quota));
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var before = await fixture.CurrentAsync();
        fixture.Repository.ThrowNext = refused;
        var observed = await Assert.ThrowsAsync<AggregateException>(() => fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(before) }, default));
        Assert.True(ContainsOriginal(observed, quota));
        Assert.True(ContainsOriginal(observed, refused));
        Assert.Equal(0, second.ToolCalls);
        var after = await fixture.CurrentAsync();
        Assert.Equal(before.PersistenceRevision, after.PersistenceRevision);
        Assert.Equal(TaskRunAttemptState.Running, after.Attempts.Single().State);
        var close = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Frames.CloseAndDrainAsync());
        Assert.True(ContainsOriginal(close, quota));
        Assert.Equal(1, fixture.Authority.Leases.Single().Disposals);
    }

    [Fact]
    public async Task StaleCanonicalObservationCannotCallProviderOrFallback()
    {
        var first = new Provider("first", isLocal: true);
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var before = await fixture.CurrentAsync();
        var stale = fixture.Context(before) with { PersistenceRevision = before.PersistenceRevision - 1 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = stale }, default));
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(0, second.CompletionCalls);
        Assert.Equal(before.PersistenceRevision, (await fixture.CurrentAsync()).PersistenceRevision);
        await fixture.Frames.CloseAndDrainAsync();
    }

    [Fact]
    public async Task QueuedMetadataDuringOriginalCatalogueWaitRefreshesSameRunWithoutGrantOrStaleAttempt()
    {
        var first = new Provider("first", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var before = await fixture.CurrentAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        first.CatalogueBody = async token =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            return new ProviderModelDescriptor[] { first.Descriptor };
        };
        var pending = fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(before) }, default);
        Exception? controlFailure = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Coordinator.SubmitFollowUpAsync(before.TaskId, "afterwards retain mid-request queue", TaskFollowUpMode.Queue, [], [], default);
            var queued = await fixture.CurrentAsync();
            Assert.True(queued.PersistenceRevision > before.PersistenceRevision);
            Assert.False(pending.IsCompleted);
            release.TrySetResult();
            var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            var after = await fixture.CurrentAsync();
            Assert.Equal(before.TaskId, after.TaskId);
            Assert.Equal(before.ExecutionId, after.ExecutionId);
            Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
            Assert.Equal(JsonSerializer.Serialize(queued.Queue), JsonSerializer.Serialize(after.Queue));
            Assert.Equal(JsonSerializer.Serialize(before.Plan.Single()), JsonSerializer.Serialize(after.Plan.Single()));
            Assert.Equal(2, after.Attempts.Count);
            Assert.Same(second.Descriptor, response.EffectiveModel);
        }
        catch (Exception failure) { controlFailure = failure; }
        finally { release.TrySetResult(); }
        await DrainControlOriginalsAsync(fixture, pending, controlFailure);
        Assert.All(fixture.Authority.Leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public async Task FailureCasObserverQueuedMetadataRefreshesSameAttemptBeforeAuthorizedResume()
    {
        var first = new Provider("first", isLocal: true, tools: _ => Task.FromException<OllamaToolResponse>(Quota()));
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var before = await fixture.CurrentAsync();
        TaskExecutionSnapshot? queuedFromObserver = null;
        var observed = 0;
        fixture.Coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Attempts.LastOrDefault()?.State == TaskRunAttemptState.Failed
                && Interlocked.CompareExchange(ref observed, 1, 0) == 0)
            {
                fixture.Coordinator.SubmitFollowUpAsync(snapshot.TaskId,
                    "retain queue submitted by failure CAS observer", TaskFollowUpMode.Queue, [], [], default).GetAwaiter().GetResult();
                queuedFromObserver = fixture.CurrentAsync().GetAwaiter().GetResult();
            }
        };
        var pending = fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(before) }, default);
        Exception? controlFailure = null;
        try
        {
            var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            var after = await fixture.CurrentAsync();
            Assert.NotNull(queuedFromObserver);
            Assert.Equal(1, observed);
            Assert.Equal(before.TaskId, after.TaskId);
            Assert.Equal(before.ExecutionId, after.ExecutionId);
            Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
            Assert.Equal(JsonSerializer.Serialize(queuedFromObserver.Queue), JsonSerializer.Serialize(after.Queue));
            Assert.Equal(JsonSerializer.Serialize(before.Plan.Single()), JsonSerializer.Serialize(after.Plan.Single()));
            Assert.Equal(2, after.Attempts.Count);
            Assert.True(after.PersistenceRevision > queuedFromObserver.PersistenceRevision);
            Assert.Same(second.Descriptor, response.EffectiveModel);
        }
        catch (Exception failure) { controlFailure = failure; }
        await DrainControlOriginalsAsync(fixture, pending, controlFailure);
        Assert.All(fixture.Authority.Leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public async Task IssuedOpenRouterCreditExhaustionUsesFreshAuthorizedLocalAttemptInSameTaskRun()
    {
        var exhausted = new HttpRequestException("Synthetic original OpenRouter credit exhaustion", null, HttpStatusCode.PaymentRequired);
        var first = new Provider("openrouter", tools: _ => Task.FromException<OllamaToolResponse>(exhausted));
        var second = new Provider("local", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second], allowSyntheticCloud: true);
        var before = await fixture.CurrentAsync();
        var pending = fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(before) }, default);
        Exception? controlFailure = null;
        try
        {
            var response = await pending;
            var after = await fixture.CurrentAsync();
            Assert.Equal(before.TaskId, after.TaskId);
            Assert.Equal(before.ExecutionId, after.ExecutionId);
            Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
            Assert.Equal(JsonSerializer.Serialize(before.Plan.Single()), JsonSerializer.Serialize(after.Plan.Single()));
            Assert.Equal(JsonSerializer.Serialize(before.Queue), JsonSerializer.Serialize(after.Queue));
            Assert.Equal(2, after.Attempts.Count);
            Assert.Equal("PROVIDER_CREDITS_EXHAUSTED", after.Attempts[0].Failure?.Code);
            Assert.Equal(402, after.Attempts[0].Failure?.HttpStatus);
            Assert.True(after.Attempts[0].Candidate.UsesCloud);
            Assert.False(after.Attempts[1].Candidate.UsesCloud);
            Assert.Equal(2, fixture.Authority.Leases.Count);
            Assert.NotSame(fixture.Authority.Leases[0], fixture.Authority.Leases[1]);
            Assert.Same(second.Descriptor, response.EffectiveModel);
        }
        catch (Exception failure) { controlFailure = failure; }
        await DrainControlOriginalsAsync(fixture, pending, controlFailure);
        Assert.All(fixture.Authority.Leases, lease => Assert.Equal(1, lease.Disposals));
    }

    [Fact]
    public async Task IssuedOpenRouterCreditExhaustionWithoutEligibleFallbackSuspendsSameRunAndRetainsAcceptedWork()
    {
        var exhausted = new HttpRequestException("Synthetic original no credits", null, HttpStatusCode.PaymentRequired);
        var first = new Provider("openrouter", tools: _ => Task.FromException<OllamaToolResponse>(exhausted));
        var fixture = await CanonicalFixture.CreateAsync([first], allowSyntheticCloud: true);
        var before = await fixture.CurrentAsync();
        var original = fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(before) }, default);
        var originalFailure = await Record.ExceptionAsync(() => original);
        var originalDrainFailure = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        var observed = Assert.IsType<InvalidOperationException>(originalFailure);
        Assert.Null(originalDrainFailure);
        Assert.Same(exhausted, observed.InnerException);
        var after = await fixture.CurrentAsync();
        Assert.Equal(before.TaskId, after.TaskId);
        Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
        Assert.Equal(JsonSerializer.Serialize(before.Plan.Single()), JsonSerializer.Serialize(after.Plan.Single()));
        Assert.Equal(TaskExecutionLifecycle.Suspended, after.State);
        Assert.Single(after.Attempts);
        Assert.Equal("PROVIDER_CREDITS_EXHAUSTED", after.Attempts[0].Failure?.Code);
        Assert.Equal(1, fixture.Authority.Leases.Single().Disposals);
    }

    [Fact]
    public Task IssuedOpenRouterInvalidCredentialsRefusesFallback() => AssertProviderStatusRefusesFallbackAsync("openrouter", HttpStatusCode.Unauthorized);

    [Fact]
    public Task IssuedOpenRouterPermissionOrGuardrailDenialRefusesFallback() => AssertProviderStatusRefusesFallbackAsync("openrouter", HttpStatusCode.Forbidden);

    [Fact]
    public Task ArbitraryOtherProviderPaymentRequiredRefusesFallback() => AssertProviderStatusRefusesFallbackAsync("other", HttpStatusCode.PaymentRequired);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OtherRequestedCatalogueFailureCannotFailOrRetargetAnIssuedOriginalAttempt(bool retainOtherSelection)
    {
        var first = new Provider("first", isLocal: true);
        var other = new Provider("openai-compatible", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, other]);
        var before = await fixture.CurrentAsync();
        if (retainOtherSelection)
            await fixture.Authority.CaptureSelectedRouteAsync(before, other.Descriptor, [ToolCapability.Text], [], default);
        var catalogueCause = new HttpRequestException("Actual other-provider catalogue unavailable", null, HttpStatusCode.ServiceUnavailable);
        other.CatalogueFailure = catalogueCause;
        var original = fixture.Client.CompleteAsync(Chat(other) with { ExecutionContext = fixture.Context(before) }, default);
        var failure = await Record.ExceptionAsync(() => original);
        var drainFailure = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        Assert.Null(drainFailure);
        Exception refusal = retainOtherSelection
            ? Assert.IsType<UnauthorizedAccessException>(failure)
            : Assert.IsType<InvalidOperationException>(failure);
        Assert.Same(catalogueCause, refusal.InnerException);
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(0, other.CompletionCalls);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
        Assert.Single(fixture.Authority.Leases);
        Assert.Equal(1, fixture.Authority.Leases.Single().Disposals);
    }

    [Fact]
    public async Task HealthyNarrowerFrameReusesSameIssuedAttemptAndLeaseWithoutDroppingAcceptedWork()
    {
        var first = new Provider("first", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first]);
        var before = await fixture.CurrentAsync();
        Task original = fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default);
        Exception? controlFailure = null;
        try
        {
            Assert.Equal("first result", await (Task<string>)original);
            var afterNarrower = await fixture.CurrentAsync();
            original = fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(afterNarrower) }, default);
            Assert.Equal("first result", (await (Task<OllamaToolResponse>)original).Content);
            var after = await fixture.CurrentAsync();
            Assert.Equal(before.TaskId, after.TaskId);
            Assert.Equal(before.ExecutionId, after.ExecutionId);
            Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
            Assert.Equal(JsonSerializer.Serialize(before.Plan.Single()), JsonSerializer.Serialize(after.Plan.Single()));
            Assert.Single(after.Attempts);
            Assert.Equal(before.Attempts.Single().Id, after.Attempts.Single().Id);
            Assert.Single(fixture.Authority.Leases);
            Assert.Same(fixture.Admission.Lease, fixture.Authority.Leases.Single());
            Assert.Equal(0, fixture.Authority.Leases.Single().Disposals);
            Assert.Equal(1, first.CompletionCalls);
            Assert.Equal(1, first.ToolCalls);
        }
        catch (Exception error) { controlFailure = original.Exception is { } payload ? payload : error; }
        finally { await DrainControlOriginalsAsync(fixture, original, controlFailure); }
        Assert.Equal(1, fixture.Authority.Leases.Single().Disposals);
    }

    [Fact]
    public async Task ExpandedFrameCapabilitiesCannotBroadenAnIssuedAttemptEvenWhenModelSupportsThem()
    {
        var first = new Provider("first", isLocal: true,
            capabilities: new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools, ToolCapability.Streaming, ToolCapability.Vision });
        var fixture = await CanonicalFixture.CreateAsync([first]);
        var before = await fixture.CurrentAsync();
        var request = Chat(first) with { Messages = [new("user", "Actual narrower-issued scope refuses an added image", ["synthetic-image"])], ExecutionContext = fixture.Context(before) };
        var original = fixture.Client.CompleteAsync(request, default);
        var failure = await Record.ExceptionAsync(() => original);
        var drainFailure = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        Assert.Null(drainFailure);
        Assert.IsType<UnauthorizedAccessException>(failure);
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
        Assert.Single(fixture.Authority.Leases);
        Assert.Equal(1, fixture.Authority.Leases.Single().Disposals);
    }

    private static async Task AssertProviderStatusRefusesFallbackAsync(string providerId, HttpStatusCode status)
    {
        var cause = new HttpRequestException("Synthetic original provider refusal", null, status);
        var first = new Provider(providerId, tools: _ => Task.FromException<OllamaToolResponse>(cause));
        var second = new Provider("local", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second], allowSyntheticCloud: true);
        var before = await fixture.CurrentAsync();
        var original = fixture.Client.ChatWithToolsAsync(Tools(first) with { ExecutionContext = fixture.Context(before) }, default);
        var originalFailure = await Record.ExceptionAsync(() => original);
        var originalDrainFailure = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        var observed = Assert.IsType<HttpRequestException>(originalFailure);
        Assert.Same(cause, observed);
        Assert.Equal(0, second.ToolCalls);
        var after = await fixture.CurrentAsync();
        Assert.Equal(before.TaskId, after.TaskId);
        Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(before.LastCheckpointActionId, after.LastCheckpointActionId);
        Assert.Equal(JsonSerializer.Serialize(before.Plan.Single()), JsonSerializer.Serialize(after.Plan.Single()));
        Assert.Single(after.Attempts);
        var drain = Assert.IsType<HttpRequestException>(originalDrainFailure);
        Assert.Same(cause, drain); // Original unacknowledged refusal is retained, not waived by a generic failure string.
        Assert.NotEqual("PROVIDER_CREDITS_EXHAUSTED", after.Attempts[0].Failure?.Code);
    }

    [Fact]
    public async Task CanonicalRemoteWithoutOriginalContextSourceRefusesBeforeRawProviderOrFallback()
    {
        var first = new Provider("first");
        var second = new Provider("local", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second], allowSyntheticCloud: true, useSyntheticContext: false);
        var before = await fixture.CurrentAsync();
        var original = fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default);
        var failure = await Record.ExceptionAsync(() => original);
        var cleanup = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        var refusal = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("context owner is unavailable", refusal.Message, StringComparison.Ordinal);
        Assert.Same(refusal, cleanup);
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(0, second.CompletionCalls);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
        Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
    }

    [Fact]
    public async Task ActualContextCleanupBlocksQuotaRecoveryAndRetainsEveryOriginalCause()
    {
        var quota = Quota();
        var cleanup = new IOException("Actual context cleanup failed after actual raw quota body");
        var first = new Provider("first", completion: _ => Task.FromException<string>(quota));
        var second = new Provider("local", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second], allowSyntheticCloud: true);
        var source = Assert.IsType<SyntheticContextAuthority>(fixture.ContextAuthority);
        source.ActualDispose = Task.FromException(cleanup);
        var before = await fixture.CurrentAsync();
        var original = fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default);
        var failure = await Record.ExceptionAsync(() => original);
        var drain = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        Assert.NotNull(failure);
        Assert.NotNull(drain);
        Assert.True(ContainsOriginal(failure!, quota));
        Assert.True(ContainsOriginal(failure!, cleanup));
        Assert.True(ContainsOriginal(drain!, quota));
        Assert.True(ContainsOriginal(drain!, cleanup));
        Assert.Equal(1, source.InvocationStarts);
        Assert.Equal(1, source.DisposeCalls);
        Assert.Equal(1, first.CompletionCalls);
        Assert.Equal(0, second.CompletionCalls);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
        Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
    }

    [Fact]
    public async Task CompoundActualContextAcquireFaultsNeverBecomeProviderQuotaAcknowledgments()
    {
        var firstCause = new UnauthorizedAccessException("Actual context owner refused disclosure");
        var secondCause = new IOException("Actual context acquisition sibling");
        var actualAcquire = new TaskCompletionSource<ITaskRunProviderContextFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        actualAcquire.SetException([firstCause, secondCause]);
        var first = new Provider("first");
        var second = new Provider("local", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second], allowSyntheticCloud: true);
        var source = Assert.IsType<SyntheticContextAuthority>(fixture.ContextAuthority);
        source.ActualAcquire = actualAcquire.Task;
        var before = await fixture.CurrentAsync();
        var original = fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default);
        var failure = await Record.ExceptionAsync(() => original);
        var drain = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        Assert.NotNull(failure);
        Assert.NotNull(drain);
        Assert.True(ContainsOriginal(failure!, firstCause));
        Assert.True(ContainsOriginal(failure!, secondCause));
        Assert.True(ContainsOriginal(drain!, firstCause));
        Assert.True(ContainsOriginal(drain!, secondCause));
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(0, second.CompletionCalls);
        Assert.Equal(0, source.InvocationStarts);
        Assert.Equal(0, source.DisposeCalls);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
        Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
    }

    [Fact]
    public async Task DetachedCanonicalWireConservesCapturedTranscriptAndNestedSchemaDuringCallerMutation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Provider("first", tools: _ => { entered.TrySetResult(); return release.Task; });
        var fixture = await CanonicalFixture.CreateAsync([first], allowSyntheticCloud: true);
        var source = Assert.IsType<SyntheticContextAuthority>(fixture.ContextAuthority);
        var before = await fixture.CurrentAsync();
        var originalMessages = new List<OllamaToolTurn> { new("user", "Actual captured prompt") };
        var nested = new Dictionary<string, object> { ["type"] = "string" };
        var properties = new Dictionary<string, object> { ["nested"] = nested };
        var originalTools = new List<OllamaToolDefinition> { new("synthetic_safe_tool", "Controlled original schema", properties, ["nested"]) };
        var request = new OllamaToolRequest(first.Descriptor.Key, originalMessages, originalTools, EffortLevel.Medium)
        { ExecutionContext = fixture.Context(before) };
        var original = fixture.Client.ChatWithToolsAsync(request, default);
        Exception? controlFailure = null;
        try
        {
            await Task.WhenAny(entered.Task, original);
            if (!entered.Task.IsCompleted) await original;
            var actual = Assert.IsType<OllamaToolRequest>(first.LastToolRequest);
            Assert.NotSame(request, actual);
            Assert.NotSame(originalMessages, actual.Messages);
            Assert.NotSame(originalTools, actual.Tools);
            originalMessages[0] = new("user", "Must never enter the already captured wire");
            nested["type"] = "changed after actual raw start";
            properties["new_unapproved"] = true;
            originalTools.Clear();
            Assert.Equal("Actual captured prompt", Assert.Single(actual.Messages).Content);
            var actualDefinition = Assert.Single(actual.Tools);
            Assert.Single(actualDefinition.Properties);
            Assert.Equal("string", Assert.IsType<JsonElement>(actualDefinition.Properties["nested"]).GetProperty("type").GetString());
            Assert.Equal("nested", Assert.Single(actualDefinition.Required));
            Assert.Equal(first.Descriptor.Name, actual.Model);
            Assert.Equal(before.TaskId, actual.ExecutionContext!.TaskId);
            release.TrySetResult(new("Actual result", []));
            var response = await original;
            Assert.Equal("Actual result", response.Content);
            Assert.Same(first.Descriptor, response.EffectiveModel);
            Assert.Equal(1, source.InvocationStarts);
        }
        catch (Exception failure) { controlFailure = failure; }
        finally { release.TrySetResult(new("Actual result", [])); }
        await DrainControlOriginalsAsync(fixture, original, controlFailure);
        Assert.Equal(1, source.DisposeCalls);
        Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
    }

    [Fact]
    public async Task SynchronousQuotaWithoutActualRawTaskRetainsCauseButCannotWaiveOriginalAdmissionFailure()
    {
        var quota = Quota();
        var first = new Provider("first", isLocal: true, completion: _ => throw quota);
        var second = new Provider("second", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second]);
        var before = await fixture.CurrentAsync();
        var original = fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default);
        var failure = await Record.ExceptionAsync(() => original);
        var drain = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        var refusal = Assert.IsType<InvalidOperationException>(failure);
        Assert.Same(quota, refusal.InnerException);
        Assert.Same(quota, drain);
        Assert.Equal(1, first.CompletionCalls);
        Assert.Equal(0, second.CompletionCalls);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
        Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
    }

    [Fact]
    public async Task ActualAcquiredContextWithoutInvocationFenceClosesBeforeAnyRawProviderBody()
    {
        var first = new Provider("first");
        var second = new Provider("local", isLocal: true);
        var fixture = await CanonicalFixture.CreateAsync([first, second], allowSyntheticCloud: true);
        var source = Assert.IsType<SyntheticContextAuthority>(fixture.ContextAuthority);
        var resource = new UnfencedContextFrame();
        source.ActualAcquire = Task.FromResult<ITaskRunProviderContextFrame>(resource);
        var before = await fixture.CurrentAsync();
        var original = fixture.Client.CompleteAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default);
        var failure = await Record.ExceptionAsync(() => original);
        var drain = await Record.ExceptionAsync(() => fixture.Frames.CloseAndDrainAsync());
        var refusal = Assert.IsType<UnauthorizedAccessException>(failure);
        Assert.Contains("invocation permission fence", refusal.Message, StringComparison.Ordinal);
        Assert.Same(refusal, drain);
        Assert.Equal(1, resource.Disposals);
        Assert.Equal(0, resource.Revalidations);
        Assert.Equal(0, first.CompletionCalls);
        Assert.Equal(0, second.CompletionCalls);
        Assert.Equal(0, source.InvocationStarts);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.CurrentAsync()));
        Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
    }

    [Fact]
    public async Task CanonicalRemoteStreamFencesFiniteFactoryButNeverSynchronousEnumerationOrCleanup()
    {
        SyntheticContextAuthority? source = null;
        var stream = new SynchronousFenceProbeStream(() => source!.InvocationActive);
        var first = new Provider("first", stream: _ =>
        {
            Assert.True(source!.InvocationActive);
            return stream;
        });
        var fixture = await CanonicalFixture.CreateAsync([first], allowSyntheticCloud: true,
            initialRequiredCapabilities: [ToolCapability.Text, ToolCapability.Streaming]);
        source = Assert.IsType<SyntheticContextAuthority>(fixture.ContextAuthority);
        var before = await fixture.CurrentAsync();
        async Task<string> ReadOriginalAsync()
        {
            var pieces = new List<string>();
            await foreach (var piece in fixture.Client.StreamChatAsync(Chat(first) with { ExecutionContext = fixture.Context(before) }, default))
                pieces.Add(piece);
            return string.Concat(pieces);
        }
        var original = ReadOriginalAsync();
        Exception? controlFailure = null;
        try
        {
            Assert.Equal("Alpha beta", await original);
            Assert.Equal(1, source.InvocationStarts);
            Assert.False(source.InvocationActive);
            Assert.Equal(3, stream.MoveCalls);
            Assert.Equal(1, stream.Disposals);
        }
        catch (Exception failure) { controlFailure = failure; }
        await DrainControlOriginalsAsync(fixture, original, controlFailure);
        Assert.Equal(1, first.StreamCalls);
        Assert.Equal(1, source.DisposeCalls);
        Assert.Equal(1, Assert.Single(fixture.Authority.Leases).Disposals);
    }

    private sealed class UnfencedContextFrame : ITaskRunProviderContextFrame
    {
        public int Disposals { get; private set; }
        public int Revalidations { get; private set; }
        public ValueTask RevalidateAsync(CancellationToken token) { Revalidations++; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class SynchronousFenceProbeStream(Func<bool> invocationActive) : IAsyncEnumerable<string>, IAsyncEnumerator<string>
    {
        public int MoveCalls { get; private set; }
        public int Disposals { get; private set; }
        public string Current => MoveCalls == 1 ? "Alpha" : " beta";
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token = default)
        { Assert.True(invocationActive()); return this; }
        public ValueTask<bool> MoveNextAsync()
        { Assert.False(invocationActive()); MoveCalls++; return ValueTask.FromResult(MoveCalls <= 2); }
        public ValueTask DisposeAsync()
        { Assert.False(invocationActive()); Disposals++; return ValueTask.CompletedTask; }
    }

    private static async Task DrainControlOriginalsAsync(CanonicalFixture fixture, Task originalRequest, Exception? controlFailure)
    {
        var failures = new List<Exception>();
        void Retain(Exception failure)
        {
            if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
        }
        if (controlFailure is not null) Retain(controlFailure);
        try { await originalRequest; }
        catch (Exception requestFailure)
        {
            if (originalRequest.Exception is { } payload)
                foreach (var cause in payload.InnerExceptions) Retain(cause);
            else Retain(requestFailure);
        }
        try { await fixture.Frames.CloseAndDrainAsync(); }
        catch (Exception drainFailure) { Retain(drainFailure); }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Control and every original request/drain cause retained.", failures);
    }

    private static async IAsyncEnumerable<string> CancelFaultStream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token,
        Exception callback, TaskCompletionSource cleanup)
    {
        using var registration = token.Register(() => throw callback);
        try
        {
            yield return "first chunk";
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        finally { await Task.Yield(); cleanup.TrySetResult(); }
    }

    private static bool ContainsOriginal(Exception observed, Exception original) => ReferenceEquals(observed, original)
        || observed is AggregateException aggregate && aggregate.InnerExceptions.Any(child => ContainsOriginal(child, original));
    private static HttpRequestException Quota() => new("Original provider rate limit", null, HttpStatusCode.TooManyRequests);
    private static OllamaChatRequest Chat(Provider selected) => new(selected.Descriptor.Key, [new("user", "Original prompt")], EffortLevel.Medium);
    private static OllamaToolRequest Tools(Provider selected) => new(selected.Descriptor.Key,
        [new("user", "Original prompt")], [], EffortLevel.Medium);

    private static ResilientProviderRoutingModelClient Client(IReadOnlyList<Provider> actual, Sink? sink = null,
        TaskExecutionCoordinator? coordinator = null, ITaskRunSelectedRouteCapture? capture = null, ITaskRunOriginalFrameOwner? frames = null,
        ModelPermissionEvaluator? permissions = null, ITaskRunProviderContextAuthority? contextAuthority = null)
    {
        var privacy = new Privacy();
        var registry = new ModelProviderRegistry(actual);
        var configs = new Configurations(actual.Select(provider => new ProviderConfiguration(provider.Id,
            provider.IsLocal ? ModelProviderKind.Ollama : ModelProviderKind.OpenAICompatible,
            provider.Id, "https://example.invalid/", true, provider.IsLocal, true,
            new Dictionary<string, string> { ["fallback-chain"] = string.Join(',', actual.Where(item => item != provider).Select(item => item.Descriptor.Key)) },
            DateTimeOffset.UnixEpoch)).ToArray());
        return new(new ProviderRoutingModelClient(new EmptyLocal(), registry, privacy), registry, configs, privacy,
            executionEvents: sink, taskCoordinator: coordinator, routeCapture: capture, originalFrames: frames, modelPermissions: permissions, taskContextAuthority: contextAuthority);
    }

    private sealed class Sink : IExecutionEventSink
    {
        public List<ExecutionEvent> Events { get; } = [];
        public bool TryPublish(ExecutionEvent value) { Events.Add(value); return true; }
    }
    private sealed class PermissionStore(ModelPermissionPolicy actual) : IModelPermissionStore
    {
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(actual); }
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
    }
    private sealed class Configurations(IReadOnlyList<ProviderConfiguration> all) : IProviderConfigurationStore
    {
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult(all);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult(all.FirstOrDefault(item => item.Id == id));
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Provider : IModelProvider
    {
        private readonly Func<CancellationToken, Task<string>> _completion;
        private readonly Func<CancellationToken, Task<OllamaToolResponse>> _tools;
        private readonly Func<CancellationToken, IAsyncEnumerable<string>> _stream;
        public Provider(string id, bool isLocal = false, Func<CancellationToken, Task<string>>? completion = null,
            Func<CancellationToken, Task<OllamaToolResponse>>? tools = null,
            Func<CancellationToken, IAsyncEnumerable<string>>? stream = null, IReadOnlySet<ToolCapability>? capabilities = null)
        {
            Id = id; IsLocal = isLocal;
            Descriptor = new(id, isLocal, new("model-" + id, 0, "synthetic", "", "",
                capabilities ?? new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools, ToolCapability.Streaming }, DateTimeOffset.UnixEpoch));
            _completion = completion ?? (_ => Task.FromResult(id + " result"));
            _tools = tools ?? (_ => Task.FromResult(new OllamaToolResponse(id + " result", [])));
            _stream = stream ?? Success;
        }
        public string Id { get; }
        public string DisplayName => Id;
        public ModelProviderKind Kind => IsLocal ? ModelProviderKind.Ollama : Id == "openrouter" ? ModelProviderKind.OpenRouter : ModelProviderKind.OpenAICompatible;
        public bool IsLocal { get; }
        public bool CanManageModels => false;
        public ProviderModelDescriptor Descriptor { get; }
        public int CompletionCalls { get; private set; }
        public int ToolCalls { get; private set; }
        public int StreamCalls { get; private set; }
        public OllamaChatRequest? LastChatRequest { get; private set; }
        public OllamaToolRequest? LastToolRequest { get; private set; }
        public Exception? CatalogueFailure { get; set; }
        public Func<CancellationToken, Task<IReadOnlyList<ProviderModelDescriptor>>>? CatalogueBody { get; set; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(Id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => CatalogueBody is { } original
            ? original(token) : CatalogueFailure is { } failure
                ? Task.FromException<IReadOnlyList<ProviderModelDescriptor>>(failure) : Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Descriptor]);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) { CompletionCalls++; LastChatRequest = request; return _completion(token); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) { ToolCalls++; LastToolRequest = request; return _tools(token); }
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) { StreamCalls++; LastChatRequest = request; return _stream(token); }
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteModelAsync(string model, CancellationToken token) => throw new NotSupportedException();
        private async IAsyncEnumerable<string> Success([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        { await Task.Yield(); token.ThrowIfCancellationRequested(); yield return Id + " result"; }
    }
    private sealed class FailedStream(Exception body, Exception cleanup) : IAsyncEnumerable<string>, IAsyncEnumerator<string>
    {
        public string Current => throw new InvalidOperationException();
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token = default) => this;
        public ValueTask<bool> MoveNextAsync() => new(Task.FromException<bool>(body));
        public ValueTask DisposeAsync() => new(Task.FromException(cleanup));
    }

    /// <summary>Fixture issuer only; real coordinator and original frame owner are exercised unchanged.</summary>
    private sealed record CanonicalFixture(TaskExecutionCoordinator Coordinator, MemoryRepository Repository,
        FixtureAuthority Authority, TaskRunOriginalFrameOwner Frames, Sink Events,
        TaskExecutionSnapshot Task, TaskRunAttemptAdmission Admission, IProviderModelClient Client, SyntheticContextAuthority? ContextAuthority)
    {
        public static async Task<CanonicalFixture> CreateAsync(IReadOnlyList<Provider> providers, bool allowSyntheticCloud = false, bool useSyntheticContext = true,
            IReadOnlyCollection<ToolCapability>? initialRequiredCapabilities = null)
        {
            var repository = new MemoryRepository();
            var events = new Sink();
            var authority = new FixtureAuthority(providers, allowSyntheticCloud);
            TaskExecutionCoordinator? coordinator = null;
            var frames = new TaskRunOriginalFrameOwner((task, run, attempt, token) =>
                coordinator!.GetIssuedAttemptAsync(task, run, attempt, token));
            coordinator = new(repository, events, admissionAuthority: authority, runtimeSettlement: frames);
            var task = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(),
                "Synthetic original task/run recovery controls", TaskExecutionDurability.RecoverableCheckpoint, [], default);
            var candidate = await authority.CaptureSelectedRouteAsync(task, providers[0].Descriptor,
                initialRequiredCapabilities ?? [ToolCapability.Text, ToolCapability.Tools], [], default);
            var admission = await coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId, candidate, default);
            await frames.RegisterOriginalAttemptAsync(admission, default);
            task = await coordinator.MarkAttemptRunningAsync(task.TaskId, task.ExecutionId, admission.AttemptId, default);
            var accepted = Guid.NewGuid();
            await coordinator.RegisterActionAsync(task.TaskId, accepted, null, "Synthetic independently accepted owner action",
                TaskActionInterruptionPolicy.AtomicCommit, null, [], default, admission.AttemptId);
            task = await coordinator.AcceptActionAsync(task.TaskId, task.ExecutionId, admission.AttemptId,
                accepted, "synthetic-original-owner-receipt", default);
            var contextAuthority = allowSyntheticCloud && useSyntheticContext ? new SyntheticContextAuthority(task) : null;
            var rawRouter = ResilientProviderRoutingRecoveryTests.Client(providers, events, coordinator, authority, frames,
                contextAuthority: contextAuthority);
            IProviderModelClient client = contextAuthority is null ? rawRouter : new TrustedSyntheticContextProducer(rawRouter, contextAuthority);
            return new(coordinator, repository, authority, frames, events, task, admission, client, contextAuthority);
        }
        public async Task<TaskExecutionSnapshot> CurrentAsync() => (await Repository.GetAsync(Task.TaskId, default))!;
        public ProviderExecutionContext Context(TaskExecutionSnapshot current) => new(current.TaskId, current.ContextId,
            current.ExecutionId, current.Attempts.Last().Id, current.PersistenceRevision)
        { RequestedCandidate = current.Attempts.First().Candidate, SelectedCandidate = current.Attempts.Last().Candidate };
    }
    // This fixture issuer/producer is deliberately synthetic. It exercises the actual production
    // router/frame/coordinator without certifying real actor, resource disclosure or paid-use policy.
    // The real configured context source and central fence have separately owned controls.
    private sealed class SyntheticContextAuthority(TaskExecutionSnapshot actualTask) : ITaskRunProviderContextAuthority
    {
        private readonly Dictionary<object, string> _captured = new(ReferenceEqualityComparer.Instance);
        public Task<ITaskRunProviderContextFrame>? ActualAcquire { get; set; }
        public Task? ActualDispose { get; set; }
        public int InvocationStarts { get; private set; }
        public bool InvocationActive { get; private set; }
        public int DisposeCalls { get; private set; }
        private static string Payload(OllamaChatRequest request) => JsonSerializer.Serialize(request with { Model = "", ExecutionContext = null });
        private static string Payload(OllamaToolRequest request) => JsonSerializer.Serialize(request with { Model = "", ExecutionContext = null });
        public void Capture(OllamaChatRequest original) => Capture(original, original.ExecutionContext, Payload(original));
        public void Capture(OllamaToolRequest original) => Capture(original, original.ExecutionContext, Payload(original));
        private void Capture(object original, ProviderExecutionContext? context, string payload)
        {
            if (context is null || context.TaskId != actualTask.TaskId || context.ContextId != actualTask.ContextId || context.ExecutionId != actualTask.ExecutionId)
                throw new UnauthorizedAccessException("Synthetic producer cannot capture another task/run.");
            _captured.Add(original, payload); // Private actual original object, never reconstructed from an ID.
        }
        public ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(TaskRunAttemptAdmission same,
            TaskExecutionSnapshot current, OllamaChatRequest original, OllamaChatRequest routed, CancellationToken token) =>
            Acquire(same, current, original, routed.ExecutionContext, Payload(original), Payload(routed), token);
        public ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(TaskRunAttemptAdmission same,
            TaskExecutionSnapshot current, OllamaToolRequest original, OllamaToolRequest routed, CancellationToken token) =>
            Acquire(same, current, original, routed.ExecutionContext, Payload(original), Payload(routed), token);
        private ValueTask<ITaskRunProviderContextFrame> Acquire(TaskRunAttemptAdmission same, TaskExecutionSnapshot current,
            object original, ProviderExecutionContext? routed, string originalPayload, string routedPayload, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_captured.TryGetValue(original, out var captured) || captured != originalPayload || captured != routedPayload ||
                same.Snapshot.TaskId != actualTask.TaskId || same.Snapshot.ExecutionId != actualTask.ExecutionId ||
                same.Lease.Owner != actualTask.OwnerBinding || current.TaskId != actualTask.TaskId || current.ExecutionId != actualTask.ExecutionId ||
                routed is null || routed.AttemptId != same.AttemptId || routed.TaskId != current.TaskId ||
                routed.ExecutionId != current.ExecutionId || routed.ContextId != current.ContextId || routed.PersistenceRevision != current.PersistenceRevision)
                throw new UnauthorizedAccessException("No exact captured synthetic context matches the original current attempt.");
            return ActualAcquire is null ? ValueTask.FromResult<ITaskRunProviderContextFrame>(new Scope(this)) : new(ActualAcquire);
        }
        private sealed class Scope(SyntheticContextAuthority source) : ITaskRunProviderContextFrame, ITaskRunProviderInvocationFence
        {
            private bool _closed;
            public ValueTask RevalidateAsync(CancellationToken token)
            { token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_closed, this); return ValueTask.CompletedTask; }
            public T RunOriginalInvocation<T>(Func<T> originalRawStart)
            {
                ObjectDisposedException.ThrowIf(_closed, this); source.InvocationStarts++;
                source.InvocationActive = true;
                try { return originalRawStart(); }
                finally { source.InvocationActive = false; }
            }
            public ValueTask DisposeAsync()
            { ObjectDisposedException.ThrowIf(_closed, this); _closed = true; source.DisposeCalls++; return new(source.ActualDispose ?? Task.CompletedTask); }
        }
    }
    private sealed class TrustedSyntheticContextProducer(IProviderModelClient raw, SyntheticContextAuthority contexts) : IProviderModelClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken token) => raw.IsAvailableAsync(token);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => raw.GetModelsAsync(token);
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken token) => raw.PullModelAsync(model, progress, token);
        public Task DeleteModelAsync(string model, CancellationToken token) => raw.DeleteModelAsync(model, token);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { contexts.Capture(request); return raw.CompleteAsync(request, token); } // SAME returned Task, no async proxy.
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { contexts.Capture(request); return raw.ChatWithToolsAsync(request, token); }
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token)
        { contexts.Capture(request); return raw.StreamChatAsync(request, token); }
    }

    private sealed class FixtureAuthority(IReadOnlyList<Provider> actualProviders, bool allowSyntheticCloud) : ITaskRunCommandAuthority, ITaskRunSelectedRouteCapture
    {
        private readonly Dictionary<(Guid Task, string Model), ProviderModelDescriptor> _retained = [];
        private readonly Dictionary<Guid, TaskExecutionOwnerBinding> _owners = [];
        public List<FixtureLease> Leases { get; } = [];
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot proposed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var owner = new TaskExecutionOwnerBinding(proposed.TaskId, proposed.ContextId, proposed.ExecutionId,
                "synthetic-server-actor", "synthetic-profile", null, null, "synthetic-current-auth", "synthetic-start-receipt");
            _owners.Add(proposed.TaskId, owner);
            return Task.FromResult(owner);
        }
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid attempt,
            TaskRunRouteCandidate candidate, Guid? previous, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_owners.TryGetValue(snapshot.TaskId, out var owner) || owner != snapshot.OwnerBinding
                || !_retained.ContainsKey((snapshot.TaskId, candidate.ProviderId + ":" + candidate.ModelId)))
                throw new UnauthorizedAccessException("Synthetic issuer has no original captured candidate.");
            var lease = new FixtureLease(owner, attempt, candidate);
            Leases.Add(lease);
            return Task.FromResult<ITaskRunAdmissionLease>(lease);
        }
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_owners.TryGetValue(snapshot.TaskId, out var owner) || owner != snapshot.OwnerBinding)
                throw new UnauthorizedAccessException("No synthetic current task command owner.");
            return Task.CompletedTask;
        }
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attempt, Guid action,
            string receipt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (receipt != "synthetic-original-owner-receipt") throw new UnauthorizedAccessException("No fixture owner receipt.");
            return Task.CompletedTask;
        }
        public async Task<TaskRunRouteCandidate> CaptureSelectedRouteAsync(TaskExecutionSnapshot snapshot,
            ProviderModelDescriptor actual, IReadOnlyCollection<ToolCapability> requirements,
            IReadOnlyCollection<RestrictedModelCapability> restrictions, CancellationToken token = default)
        {
            if (!_owners.TryGetValue(snapshot.TaskId, out var owner) || owner != snapshot.OwnerBinding)
                throw new UnauthorizedAccessException("No synthetic original task owner.");
            var provider = actualProviders.Single(value => value.Id == actual.ProviderId);
            var catalogue = await provider.GetModelsAsync(token);
            if (!provider.IsLocal && !allowSyntheticCloud || !catalogue.Contains(actual) || requirements.Any(required => !actual.Supports(required)))
                throw new UnauthorizedAccessException("Only actual fixture catalogue selections admitted by explicit synthetic policy are permitted.");
            _retained[(snapshot.TaskId, actual.Key)] = actual;
            return new("synthetic-task-selected:" + snapshot.TaskId.ToString("D"), 1, actual.ProviderId, actual.Name,
                null, !provider.IsLocal, requirements.Order().Select(value => value.ToString()).ToArray());
        }
        public ValueTask<ProviderModelDescriptor?> GetRetainedSelectionAsync(TaskExecutionSnapshot snapshot,
            string requestedKey, IReadOnlyCollection<ToolCapability> requirements, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var retained = _retained.GetValueOrDefault((snapshot.TaskId, requestedKey));
            return ValueTask.FromResult(retained is not null && requirements.All(retained.Supports) ? retained : null);
        }
    }
    private sealed class FixtureLease(TaskExecutionOwnerBinding owner, Guid attempt, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => attempt;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic-original-issued-lease";
        public int Disposals { get; private set; }
        public ValueTask RevalidateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Disposals != 0) throw new ObjectDisposedException("synthetic-original-lease"); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class MemoryRepository : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _records = [];
        public Exception? ThrowNext { get; set; }
        public Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ThrowNext is { } refused) { ThrowNext = null; throw refused; }
            var old = _records.TryGetValue(snapshot.TaskId, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null;
            if (snapshot.PersistenceRevision < 1 || old is null && snapshot.PersistenceRevision != 1
                || old is not null && (old.PersistenceRevision != snapshot.PersistenceRevision - 1
                    || old.ContextId != snapshot.ContextId || old.ExecutionId != snapshot.ExecutionId))
                throw new TaskExecutionRevisionConflictException(snapshot.TaskId, snapshot.PersistenceRevision - 1, snapshot.PersistenceRevision);
            _records[snapshot.TaskId] = JsonSerializer.Serialize(snapshot);
            return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token)
            => Task.FromResult(_records.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null);
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token)
            => (await GetResumableAsync(token)).FirstOrDefault(item => item.ContextId == id);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token)
            => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_records.Values.Select(json => JsonSerializer.Deserialize<TaskExecutionSnapshot>(json)!).ToArray());
    }
    private sealed class EmptyLocal : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(false);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token) { await Task.CompletedTask; yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteModelAsync(string model, CancellationToken token) => throw new NotSupportedException();
    }
}
