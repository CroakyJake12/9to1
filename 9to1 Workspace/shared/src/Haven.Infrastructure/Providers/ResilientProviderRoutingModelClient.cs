/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ResilientProviderRoutingModelClient.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ResilientProviderRoutingModelClient. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents resilient provider routing model client and keeps its related state and behavior together.
/// Honours the user's ordered fallback preference ahead of automatic ranking and records real
/// model switches in the Action Graph.
/// </summary>
public sealed partial class ResilientProviderRoutingModelClient(
    ProviderRoutingModelClient primary,
    IModelProviderRegistry providers,
    IProviderConfigurationStore configurations,
    IPrivacyPreferenceStore privacy,
    IModelFallbackOrderStore? fallbackOrder = null,
    IExecutionEventSink? executionEvents = null,
    TaskExecutionCoordinator? taskCoordinator = null,
    ITaskRunSelectedRouteCapture? routeCapture = null,
    ITaskRunOriginalFrameOwner? originalFrames = null,
    ModelPermissionEvaluator? modelPermissions = null,
    ITaskRunProviderContextAuthority? taskContextAuthority = null,
    IProviderCatalogueEligibility? catalogueEligibility = null) : IProviderModelClient, ITaskRunOriginalRequestFailureSource, ITaskRunOriginalToolCheckpointSelectionSource, ITaskRunOriginalToolResponseDispatchWitnessSource
{
    // Preserve the original six-argument CLR entry for already compiled ordinary clients.
    // Its absence of canonical owners conveys no Task/Run, cloud context or tool authority.
    public ResilientProviderRoutingModelClient(
        ProviderRoutingModelClient primary, IModelProviderRegistry providers,
        IProviderConfigurationStore configurations, IPrivacyPreferenceStore privacy,
        IModelFallbackOrderStore? fallbackOrder, IExecutionEventSink? executionEvents)
        : this(primary, providers, configurations, privacy, fallbackOrder, executionEvents,
            taskCoordinator: null, routeCapture: null, originalFrames: null, modelPermissions: null, taskContextAuthority: null)
    {
    }

    // Preserve the prior eleven-argument CLR entry for already compiled canonical clients.
    public ResilientProviderRoutingModelClient(ProviderRoutingModelClient primary, IModelProviderRegistry providers,
        IProviderConfigurationStore configurations, IPrivacyPreferenceStore privacy,
        IModelFallbackOrderStore? fallbackOrder, IExecutionEventSink? executionEvents,
        TaskExecutionCoordinator? taskCoordinator, ITaskRunSelectedRouteCapture? routeCapture,
        ITaskRunOriginalFrameOwner? originalFrames, ModelPermissionEvaluator? modelPermissions,
        ITaskRunProviderContextAuthority? taskContextAuthority)
        : this(primary, providers, configurations, privacy, fallbackOrder, executionEvents, taskCoordinator,
            routeCapture, originalFrames, modelPermissions, taskContextAuthority, catalogueEligibility: null) { }

    /// <summary>
    /// Reports whether available async applies to the current state.
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => primary.IsAvailableAsync(cancellationToken);
    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => primary.GetModelsAsync(cancellationToken);
    /// <summary>
    /// Performs pull model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => primary.PullModelAsync(model, progress, cancellationToken);
    /// <summary>
    /// Performs delete model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => primary.DeleteModelAsync(model, cancellationToken);

    /// <summary>Streams one actual provider frame; a shown chunk prevents cross-provider replay.</summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var required = RequiredCapabilities(request);
        var state = new RoutingState(request.ExecutionContext);
        await CaptureOriginalAttemptAsync(state, cancellationToken).ConfigureAwait(false);
        var candidates = await GetCandidatesAsync(request.Model, required, cancellationToken, context: state.Context, requireObservedLocalStreaming: true).ConfigureAwait(false);
        Exception? firstFailure = null;
        var emitted = false;
        for (var index = 0; index < candidates.Count; index++)
        {
            var selected = candidates[index];
            if (selected.CatalogueFailure is { } catalogueFailure && state.Admission is not null)
            {
                await RecordCatalogueUnavailableAsync(state, selected, catalogueFailure, cancellationToken).ConfigureAwait(false);
                firstFailure ??= catalogueFailure.Cause;
                continue;
            }
            var selectedRequirements = selected.Descriptor?.ProviderId == "llama-cpp"
                ? new HashSet<ToolCapability>(required) { ToolCapability.Streaming } : required;
            await PrepareAttemptAsync(state, selected, selectedRequirements, [], index > 0, cancellationToken).ConfigureAwait(false);
            if (index > 0) PublishFallback(request.Model, selected.Key, state.Context);
            using var frameCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
            {
                SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
            });
            Exception? observedProviderFailure = null;
            var routedRequest = CreateRoutedRequest(request, selected, state.Context);
            var producer = RunOriginalContextFrameAsync(state, request, routedRequest, async (invocationFence, token) =>
            {
                IAsyncEnumerator<string>? original = null;
                Exception? bodyFailure = null;
                Exception? cleanupFailure = null;
                try
                {
                    GuardSelectedProvider(selected);
                    // Maintained provider streams are actual async iterators: their factory and
                    // enumerator creation defer provider work until the first MoveNext invocation.
                    // Gate that SAME finite original operation, never an async loop or its await.
                    original = RawStream(selected, routedRequest, token).GetAsyncEnumerator(token);
                    var firstMove = StartRawInvocation(invocationFence, original.MoveNextAsync);
                    var move = firstMove.AsTask();
                    while (true)
                    {
                        // Consume each original ValueTask exactly once. Its direct Task siblings
                        // remain observable instead of retaining only await's first thrown cause.
                        try
                        {
                            if (!await AwaitExactTaskAsync(move).ConfigureAwait(false)) break;
                        }
                        catch (Exception failure)
                        {
                            observedProviderFailure = ObserveExactRawFailure(move, null, failure);
                            throw;
                        }
                        await channel.Writer.WriteAsync(original.Current, token).ConfigureAwait(false);
                        move = original.MoveNextAsync().AsTask();
                    }
                }
                catch (Exception failure) { bodyFailure = failure; }
                finally
                {
                    if (original is not null)
                    {
                        try
                        {
                            var cleanup = original.DisposeAsync().AsTask();
                            await AwaitExactTaskAsync(cleanup).ConfigureAwait(false);
                        }
                        catch (Exception failure) { cleanupFailure = failure; }
                    }
                    channel.Writer.TryComplete();
                }
                if (bodyFailure is not null && cleanupFailure is not null)
                    throw new AggregateException("Provider streaming and its original cleanup both failed.", bodyFailure, cleanupFailure);
                if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                if (bodyFailure is not null)
                {
                    ExceptionDispatchInfo.Capture(bodyFailure).Throw();
                }
                return true;
            }, frameCancellation.Token);
            // Admission/revalidation may fail before the raw body begins. The public reader must
            // observe that original terminal task instead of waiting on an unwritten channel.
            var completion = producer.ContinueWith(static (_, state) => ((ChannelWriter<string>)state!).TryComplete(),
                channel.Writer, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            Exception? frameFailure = null;
            var readFinished = false;
            try
            {
                while (true)
                {
                    bool available;
                    try { available = await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception failure) { frameFailure = failure; readFinished = true; break; }
                    if (!available) { readFinished = true; break; }
                    while (channel.Reader.TryRead(out var chunk))
                    {
                        emitted = true;
                        yield return chunk;
                    }
                }
            }
            finally
            {
                Exception? cancellationFailure = null;
                try { if (!readFinished || frameFailure is not null) frameCancellation.Cancel(); }
                catch (Exception failure) { cancellationFailure = failure; }
                // A throwing cancellation callback must never skip either original task join.
                Exception? completionFailure = null;
                try { await AwaitExactTaskAsync(completion).ConfigureAwait(false); }
                catch (Exception failure) { completionFailure = failure; }
                try { await AwaitExactTaskAsync(producer).ConfigureAwait(false); }
                catch (Exception failure)
                {
                    if (frameFailure is null) frameFailure = failure;
                    else if (!ReferenceEquals(frameFailure, failure))
                        frameFailure = new AggregateException("Stream observation and original frame both failed.", frameFailure, failure);
                }
                if (cancellationFailure is not null)
                    frameFailure = frameFailure is null ? cancellationFailure
                        : new AggregateException("Stream settlement and cancellation callback both failed.", frameFailure, cancellationFailure);
                if (completionFailure is not null)
                    frameFailure = frameFailure is null ? completionFailure
                        : new AggregateException("Stream settlement and completion callback both failed.", frameFailure, completionFailure);
                if (!readFinished && frameFailure is not null)
                    ExceptionDispatchInfo.Capture(frameFailure).Throw();
            }
            if (frameFailure is null) yield break;
            var originalObservation = frameFailure is AggregateException ? null : ObserveOriginalFrame(state, producer);
            var actualCause = originalObservation?.OriginalCause ?? frameFailure;
            // A final invocation-policy refusal before an actual MoveNext Task exists is an
            // admission cause, never an eligible provider-body failure acknowledgment.
            var observedRaw = observedProviderFailure is not null
                && (ReferenceEquals(actualCause, observedProviderFailure)
                    || originalObservation is not null && ReferenceEquals(originalObservation.OriginalCause, observedProviderFailure));
            if (observedRaw)
                await RecordProviderFailureAsync(state, producer, actualCause, cancellationToken, allowRecovery: !emitted,
                    originalObservation: originalObservation).ConfigureAwait(false);
            if (emitted || !observedRaw || !IsRecoverable(actualCause, cancellationToken, state.Admission?.Lease.Candidate.ProviderId))
                ExceptionDispatchInfo.Capture(actualCause).Throw();
            firstFailure ??= actualCause;
        }
        throw new InvalidOperationException("Every compatible model failed before producing output.", firstFailure);
    }

    /// <summary>Completes one finite raw provider frame before any same-run fallback admission.</summary>
    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var required = RequiredCapabilities(request);
        var state = CreateOriginalFailureRoutingState(request, request.ExecutionContext, cancellationToken, required, []);
        try
        {
            await CaptureOriginalAttemptAsync(state, cancellationToken).ConfigureAwait(false);
            var candidates = await GetCandidatesAsync(request.Model, required, cancellationToken, context: state.Context).ConfigureAwait(false);
            Exception? firstFailure = null;
            for (var index = 0; index < candidates.Count; index++)
            {
                var selected = candidates[index];
                if (selected.CatalogueFailure is { } catalogueFailure && state.Admission is not null)
                {
                    await RecordCatalogueUnavailableAsync(state, selected, catalogueFailure, cancellationToken).ConfigureAwait(false);
                    firstFailure ??= catalogueFailure.Cause;
                    continue;
                }
                await PrepareAttemptAsync(state, selected, required, [], index > 0, cancellationToken).ConfigureAwait(false);
                if (index > 0) PublishFallback(request.Model, selected.Key, state.Context);
                Exception? synchronousProviderFailure = null;
                Task<string>? originalProviderTask = null;
                Task<string>? originalFrame = null;
                var routedRequest = CreateRoutedRequest(request, selected, state.Context);
                try
                {
                    originalFrame = RunOriginalContextFrameAsync(state, request, routedRequest, (invocationFence, token) =>
                    {
                        GuardSelectedProvider(selected);
                        try { return originalProviderTask = StartRawInvocation(invocationFence, () => RawCompleteAsync(selected, routedRequest, token)); }
                        catch (Exception failure) { synchronousProviderFailure = failure; throw; }
                    }, cancellationToken);
                    return await AwaitExactTaskAsync(originalFrame).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    var originalObservation = originalFrame is null ? null : ObserveOriginalFrame(state, originalFrame);
                    var observed = originalObservation?.OriginalCause ?? ObserveExactRawFailure(originalProviderTask, synchronousProviderFailure, failure);
                    if (observed is null) throw;
                    await RecordProviderFailureAsync(state, originalFrame!, observed, cancellationToken,
                        originalObservation: originalObservation).ConfigureAwait(false);
                    if (!IsRecoverable(observed, cancellationToken, state.Admission?.Lease.Candidate.ProviderId)) ExceptionDispatchInfo.Capture(observed).Throw();
                    firstFailure ??= observed;
                }
            }
            throw CreateOriginalExhaustedRequestFailure(state, "Every compatible model failed before completing the request.", firstFailure);
        }
        catch (Exception outward) { ObserveOriginalFiniteRequestFailure(state, outward); throw; }
        finally { EndOriginalFiniteRequest(state); }
    }

    /// <summary>Preserves the original tool transcript and accepted work on authorized same-run recovery.</summary>
    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var hasPriorToolState = request.Messages.Any(message => !string.IsNullOrWhiteSpace(message.ToolName) || message.ToolCalls is { Count: > 0 });
        // Existing standalone tool conversations have no canonical settlement authority to resume.
        if (hasPriorToolState && request.ExecutionContext is null)
            return await primary.ChatWithToolsAsync(request, cancellationToken).ConfigureAwait(false);
        var required = RequiredCapabilities(request);
        var restrictions = request.Tools.Select(tool => ModelToolPermissionMap.Map(tool.Name))
            .Where(capability => capability.HasValue).Select(capability => capability!.Value).Distinct().ToArray();
        var state = CreateOriginalFailureRoutingState(request, request.ExecutionContext, cancellationToken, required, restrictions);
        try
        {
            await CaptureOriginalAttemptAsync(state, cancellationToken).ConfigureAwait(false);
            var candidates = await GetCandidatesAsync(request.Model, required, cancellationToken, restrictions, state.Context).ConfigureAwait(false);
            Exception? firstFailure = null;
            for (var index = 0; index < candidates.Count; index++)
            {
                var selected = candidates[index];
                if (selected.CatalogueFailure is { } catalogueFailure && state.Admission is not null)
                {
                    await RecordCatalogueUnavailableAsync(state, selected, catalogueFailure, cancellationToken).ConfigureAwait(false);
                    firstFailure ??= catalogueFailure.Cause;
                    continue;
                }
                await PrepareAttemptAsync(state, selected, required, restrictions, index > 0, cancellationToken).ConfigureAwait(false);
                if (index > 0) PublishFallback(request.Model, selected.Key, state.Context);
                Exception? synchronousProviderFailure = null;
                Task<OllamaToolResponse>? originalProviderTask = null;
                Task<OllamaToolResponse>? originalFrame = null;
                var routedRequest = CreateRoutedRequest(request, selected, state.Context);
                try
                {
                    originalFrame = RunOriginalContextFrameAsync(state, request, routedRequest, (invocationFence, token) =>
                    {
                        GuardSelectedProvider(selected);
                        try { return originalProviderTask = StartRawInvocation(invocationFence, () => RawToolsWithOriginalDispatchAsync(state, selected, routedRequest, token)); }
                        catch (Exception failure) { synchronousProviderFailure = failure; throw; }
                    }, cancellationToken);
                    BindOriginalToolDispatchFrame(state, originalFrame);
                    var response = await AwaitExactTaskAsync(originalFrame).ConfigureAwait(false);
                    if (response is null || response.ToolCalls is null)
                        throw new InvalidDataException("The provider returned a malformed tool response.");
                    return response with { EffectiveModel = selected.Descriptor, ExecutionContext = state.Context };
                }
                catch (Exception failure)
                {
                    var originalObservation = originalFrame is null ? null : ObserveOriginalFrame(state, originalFrame);
                    var observed = originalObservation?.OriginalCause ?? ObserveExactRawFailure(originalProviderTask, synchronousProviderFailure, failure);
                    if (observed is null) throw;
                    await RecordProviderFailureAsync(state, originalFrame!, observed, cancellationToken,
                        originalObservation: originalObservation).ConfigureAwait(false);
                    if (!IsRecoverable(observed, cancellationToken, state.Admission?.Lease.Candidate.ProviderId)) ExceptionDispatchInfo.Capture(observed).Throw();
                    firstFailure ??= observed;
                }
            }
            throw CreateOriginalExhaustedRequestFailure(state, "Every compatible model failed during the same tool conversation.", firstFailure);
        }
        catch (Exception outward) { ObserveOriginalFiniteRequestFailure(state, outward); throw; }
        finally { EndOriginalFiniteRequest(state); }
    }

    /// <summary>
    /// Retrieves candidates async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<SelectedProvider>> GetCandidatesAsync(
        string requestedModel,
        IReadOnlySet<ToolCapability> required,
        CancellationToken cancellationToken,
        IReadOnlyCollection<RestrictedModelCapability>? restrictions = null,
        ProviderExecutionContext? context = null, bool requireObservedLocalStreaming = false)
    {
        var availability = await GetEligibleModelsAsync(cancellationToken).ConfigureAwait(false);
        var descriptors = availability.Models;
        var requested = descriptors.FirstOrDefault(item => item.Matches(requestedModel));
        if (requested is null)
        {
            var providerId = ProviderId(requestedModel);
            var modelName = ModelName(requestedModel);
            requested = descriptors.FirstOrDefault(item => item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                                                            && item.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        }

        if (requested is null && context is not null && taskCoordinator is not null && routeCapture is not null)
        {
            var originalSnapshot = await taskCoordinator.GetAsync(context.TaskId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original canonical task is unavailable.");
            requested = await routeCapture.GetRetainedSelectionAsync(originalSnapshot, requestedModel, required.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        if (requested is not null && !required.All(requested.Supports))
            throw new NotSupportedException("The selected model does not support the requested capabilities.");
        var firstKey = requested is null ? requestedModel : RequestKey(requested);
        var selectedProviderId = requested?.ProviderId ?? ProviderId(requestedModel);
        var selectedConfiguration = await configurations.GetAsync(selectedProviderId, cancellationToken).ConfigureAwait(false);
        var allowCloud = !privacy.Current.LocalOnlyMode
                         && (requested?.IsLocal == false || selectedConfiguration?.AllowCloudFallback == true);
        var compatible = new List<ProviderModelDescriptor>();
        foreach (var item in descriptors.Where(item => required.All(item.Supports) && (allowCloud || item.IsLocal)))
        {
            var permitted = await IsPermittedForToolsAsync(item, restrictions ?? [], cancellationToken).ConfigureAwait(false);
            if (!permitted && ReferenceEquals(item, requested))
                throw new UnauthorizedAccessException("The requested model is denied the offered restricted tools.");
            if (permitted) compatible.Add(item);
        }
        availability.Failures.TryGetValue(selectedProviderId, out var firstCatalogueFailure);
        var result = new List<SelectedProvider> { new(firstKey, requested, firstCatalogueFailure) };

        // The user's ordered fallback preference always outranks per-provider chains and automatic ranking.
        if (fallbackOrder is not null)
        {
            foreach (var key in await fallbackOrder.GetOrderAsync(cancellationToken).ConfigureAwait(false))
            {
                var descriptor = compatible.FirstOrDefault(item => item.Matches(key));
                if (descriptor is not null) Add(descriptor);
            }
        }

        if (selectedConfiguration?.Metadata.TryGetValue("fallback-chain", out var chain) == true)
        {
            foreach (var key in chain.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var descriptor = compatible.FirstOrDefault(item => item.Matches(key));
                if (descriptor is not null) Add(descriptor);
            }
        }

        foreach (var descriptor in compatible
                     .OrderByDescending(item => item.IsLocal)
                     .ThenByDescending(item => item.Capabilities.Count)
                     .ThenByDescending(item => item.ContextWindow ?? 0)
                     .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            Add(descriptor);
        var ordered = result.DistinctBy(candidate => candidate.Key, StringComparer.OrdinalIgnoreCase)
            .Where(candidate => !requireObservedLocalStreaming || (candidate.Descriptor?.ProviderId ?? ProviderId(candidate.Key)) != "llama-cpp"
                || candidate.Descriptor?.Capabilities.Contains(ToolCapability.Streaming) == true).ToArray();
        if (catalogueEligibility is null) return ordered;
        var originalDescriptors = ordered.Where(item => item.Descriptor is not null).Select(item => item.Descriptor!).ToArray();
        var observedEligible = catalogueEligibility.ObserveOriginalCatalogueEligibility(originalDescriptors, required,
            new ModelRoutingPolicy(ModelRoutingMode.ManualFallback, PreferLocal: true, AllowCloud: allowCloud,
                PreferredModelKeys: ordered.Select(item => item.Key).ToArray(), AllowFallback: true));
        // Preserve unavailable original catalogue observations for the existing canonical failure
        // protocol. This metadata seam neither clones a selected descriptor nor adds a dispatch loop.
        return ordered.Where(item => item.Descriptor is null
            || observedEligible.Any(original => ReferenceEquals(original, item.Descriptor))).ToArray();

        void Add(ProviderModelDescriptor descriptor)
        {
            var key = RequestKey(descriptor);
            if (!key.Equals(firstKey, StringComparison.OrdinalIgnoreCase)) result.Add(new(key, descriptor));
        }
    }

    private sealed record ProviderCatalogueFailure(Task? OriginalTask, Exception Cause);
    private sealed record ProviderCatalogueAvailability(IReadOnlyList<ProviderModelDescriptor> Models,
        IReadOnlyDictionary<string, ProviderCatalogueFailure> Failures);
    private sealed record SelectedProvider(string Key, ProviderModelDescriptor? Descriptor, ProviderCatalogueFailure? CatalogueFailure = null);
    private sealed class RoutingState(ProviderExecutionContext? context)
    {
        public ProviderExecutionContext? Context { get; set; } = context;
        public TaskRunAttemptAdmission? Admission { get; set; }
        public TaskExecutionSnapshot? CurrentSnapshot { get; set; }
        public bool InitialObservationValidated { get; set; }
        public TaskExecutionOwnerBinding? OriginalOwner { get; set; }
        public OriginalRequestFailureBody? OriginalRequestFailure { get; set; }
    }

    private static string RequestKey(ProviderModelDescriptor descriptor) =>
        descriptor.ProviderId.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? descriptor.Name : descriptor.Key;

    private async Task CaptureOriginalAttemptAsync(RoutingState state, CancellationToken token)
    {
        if (state.Context is not { } context) return;
        if (taskCoordinator is null || originalFrames is null)
            throw new InvalidOperationException("The original canonical attempt custody is unavailable.");
        var snapshot = await taskCoordinator.GetAsync(context.TaskId, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original task is unavailable.");
        if (snapshot.ContextId != context.ContextId || snapshot.ExecutionId != context.ExecutionId
            || snapshot.PersistenceRevision != context.PersistenceRevision || snapshot.Attempts.LastOrDefault()?.Id != context.AttemptId)
            throw new InvalidOperationException("The provider request has a stale or different canonical task/run observation.");
        state.InitialObservationValidated = true;
        state.OriginalOwner = snapshot.OwnerBinding;
        if (context.AttemptId is not { } attemptId) return;
        var admission = await taskCoordinator.GetIssuedAttemptAsync(context.TaskId, context.ExecutionId, attemptId, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original issuer-owned attempt is unavailable.");
        await originalFrames.RegisterOriginalAttemptAsync(admission, token).ConfigureAwait(false);
        state.Admission = admission; // Custody exists BEFORE any subsequent provider catalogue callbacks.
    }

    private static async Task<T> AwaitExactTaskAsync<T>(Task<T> original)
    {
        try { return await original.ConfigureAwait(false); }
        catch
        {
            var directFaults = original.Exception;
            if (directFaults is { InnerExceptions.Count: > 1 }) ExceptionDispatchInfo.Capture(directFaults).Throw();
            throw;
        }
    }

    private static async Task AwaitExactTaskAsync(Task original)
    {
        try { await original.ConfigureAwait(false); }
        catch
        {
            var directFaults = original.Exception;
            if (directFaults is { InnerExceptions.Count: > 1 }) ExceptionDispatchInfo.Capture(directFaults).Throw();
            throw;
        }
    }

    private static Exception? ObserveExactRawFailure(Task? actualRawTask, Exception? synchronousCause, Exception frameCause)
    {
        if (ReferenceEquals(frameCause, synchronousCause)) return frameCause;
        var directFaults = actualRawTask?.Exception;
        if (directFaults is { InnerExceptions.Count: > 1 })
            return new AggregateException("The actual provider Task has multiple direct causes; no sibling is waived.", frameCause, directFaults);
        if (directFaults is { InnerExceptions.Count: 1 } && ReferenceEquals(frameCause, directFaults.InnerExceptions[0])) return frameCause;
        if (actualRawTask?.IsCanceled == true && frameCause is TaskCanceledException canceled
            && ReferenceEquals(canceled.Task, actualRawTask)) return frameCause;
        return null;
    }

    private async Task PrepareAttemptAsync(RoutingState state, SelectedProvider selected,
        IReadOnlySet<ToolCapability> required, IReadOnlyCollection<RestrictedModelCapability> restrictions,
        bool fallback, CancellationToken token)
    {
        ClearOriginalRequestFailure(state);
        token.ThrowIfCancellationRequested();
        GuardSelectedProvider(selected);
        if (selected.Descriptor is not null && restrictions.Count != 0)
        {
            var evaluator = modelPermissions ?? throw new InvalidOperationException("Actual model tool-governance authority is unavailable.");
            foreach (var restriction in restrictions)
            {
                var decision = await evaluator.EvaluateAsync(selected.Descriptor, restriction, cancellationToken: token).ConfigureAwait(false);
                if (!decision.Allowed) throw new UnauthorizedAccessException("The selected model is not permitted to use the offered restricted tools.");
            }
        }
        if (state.Context is not { } context) return;
        if (taskCoordinator is null || routeCapture is null || originalFrames is null)
            throw new InvalidOperationException("Canonical task admission and original provider-frame settlement are unavailable.");
        var actual = selected.Descriptor ?? throw new InvalidOperationException("The actual selected provider model descriptor is unavailable.");
        var snapshot = await RefreshObservedContextAsync(state, token).ConfigureAwait(false);
        context = state.Context!;
        var candidate = await routeCapture.CaptureSelectedRouteAsync(snapshot, actual, required.ToArray(), restrictions, token).ConfigureAwait(false);
        if (candidate.ProviderId != actual.ProviderId || candidate.ModelId != actual.Name || candidate.UsesCloud != !actual.IsLocal)
            throw new UnauthorizedAccessException("The issuing selector did not capture the actual chosen provider model.");
        TaskRunAttemptAdmission admission;
        if (fallback)
        {
            var previous = state.Admission ?? throw new InvalidOperationException("The original failed attempt is unavailable.");
            // This runs outside the raw provider frame. Resume seals, joins every admitted original frame,
            // retires the old issuer once, and obtains fresh authority without creating a Task or Run.
            admission = await taskCoordinator.ResumeAttemptAsync(context.TaskId, context.ExecutionId, previous.AttemptId, candidate, token).ConfigureAwait(false);
        }
        else if (context.AttemptId is { } attemptId)
        {
            admission = await taskCoordinator.GetIssuedAttemptAsync(context.TaskId, context.ExecutionId, attemptId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original issuer-owned provider attempt is unavailable.");
            if (!CurrentAttemptCoversSelection(admission.Lease.Candidate, candidate))
                throw new UnauthorizedAccessException("An active provider attempt cannot silently change its selected model.");
        }
        else
            admission = await taskCoordinator.StartAttemptAsync(context.TaskId, context.ExecutionId, candidate, token).ConfigureAwait(false);
        await originalFrames.RegisterOriginalAttemptAsync(admission, token).ConfigureAwait(false);
        await admission.Lease.RevalidateAsync(token).ConfigureAwait(false);
        var running = await taskCoordinator.MarkAttemptRunningAsync(context.TaskId, context.ExecutionId, admission.AttemptId, token).ConfigureAwait(false);
        state.Admission = admission;
        state.CurrentSnapshot = running;
        state.Context = context with
        {
            AttemptId = admission.AttemptId, PersistenceRevision = running.PersistenceRevision,
            RequestedCandidate = context.RequestedCandidate ?? admission.Lease.Candidate,
            SelectedCandidate = admission.Lease.Candidate
        };
    }

    private Task<T> RunOriginalFrameAsync<T>(RoutingState state, Func<CancellationToken, Task<T>> rawBody, CancellationToken token) =>
        state.Admission is { } admission
            ? (originalFrames ?? throw new InvalidOperationException("The original provider-frame owner is unavailable."))
                .StartOriginalFrameAsync(admission, rawBody, token)
            : rawBody(token);

    // An attempt lease authorizes a route; it never independently authorizes its actual content.
    // Canonical remote frames require the privately captured SAME caller request, a detached wire
    // payload, and the actual central invocation fence. Resource cleanup is owned outside the raw
    // provider task by the finite-frame owner and can never be acknowledged as a quota body fault.
    private Task<T> RunOriginalContextFrameAsync<T>(RoutingState state, OllamaChatRequest original,
        OllamaChatRequest routed, Func<ITaskRunProviderInvocationFence?, CancellationToken, Task<T>> rawBody, CancellationToken token) =>
        RunOriginalContextFrameAsync(state, (admission, snapshot, actualToken) =>
            (taskContextAuthority ?? throw new InvalidOperationException("The actual per-request cloud context owner is unavailable."))
                .AcquireOriginalFrameAsync(admission, snapshot, original, routed, actualToken).AsTask(), rawBody, token);

    private Task<T> RunOriginalContextFrameAsync<T>(RoutingState state, OllamaToolRequest original,
        OllamaToolRequest routed, Func<ITaskRunProviderInvocationFence?, CancellationToken, Task<T>> rawBody, CancellationToken token) =>
        RunOriginalContextFrameAsync(state, (admission, snapshot, actualToken) =>
            (taskContextAuthority ?? throw new InvalidOperationException("The actual per-request cloud context owner is unavailable."))
                .AcquireOriginalFrameAsync(admission, snapshot, original, routed, actualToken).AsTask(), rawBody, token);

    private Task<T> RunOriginalContextFrameAsync<T>(RoutingState state,
        Func<TaskRunAttemptAdmission, TaskExecutionSnapshot, CancellationToken, Task<ITaskRunProviderContextFrame>> acquire,
        Func<ITaskRunProviderInvocationFence?, CancellationToken, Task<T>> rawBody, CancellationToken token)
    {
        if (state.Admission is not { } admission || !admission.Lease.Candidate.UsesCloud)
            return RunOriginalFrameAsync(state, actualToken => rawBody(null, actualToken), token);
        var snapshot = state.CurrentSnapshot ?? throw new InvalidOperationException("The current original task snapshot is unavailable.");
        var frames = originalFrames ?? throw new InvalidOperationException("The original resource-frame owner is unavailable.");
        return frames.StartOriginalResourceFrameAsync<T, ITaskRunProviderContextFrame>(admission,
            actualToken => acquire(admission, snapshot, actualToken),
            // Missing invocation authority is resource admission failure, never a quota body cause.
            (sameFrame, actualToken) => sameFrame is ITaskRunProviderInvocationFence
                ? sameFrame.RevalidateAsync(actualToken).AsTask()
                : Task.FromException(new UnauthorizedAccessException("The actual remote invocation permission fence is unavailable.")),
            (sameFrame, actualToken) => rawBody((ITaskRunProviderInvocationFence)sameFrame, actualToken), token);
    }

    private static T StartRawInvocation<T>(ITaskRunProviderInvocationFence? actualFence, Func<T> originalRawStart) =>
        actualFence is null ? originalRawStart() : actualFence.RunOriginalInvocation(originalRawStart);

    private static OllamaChatRequest CreateRoutedRequest(OllamaChatRequest original, SelectedProvider selected,
        ProviderExecutionContext? context)
    {
        var routed = original with { Model = selected.Descriptor?.Name ?? selected.Key, ExecutionContext = context };
        if (context is null || selected.Descriptor?.IsLocal != false) return routed; // Preserve ordinary free/local request semantics.
        return routed with
        {
            Messages = Array.AsReadOnly(original.Messages.Select(message => message with
            { Images = message.Images is null ? null : Array.AsReadOnly(message.Images.ToArray()) }).ToArray())
        };
    }

    private static OllamaToolRequest CreateRoutedRequest(OllamaToolRequest original, SelectedProvider selected,
        ProviderExecutionContext? context)
    {
        var routed = original with { Model = selected.Descriptor?.Name ?? selected.Key, ExecutionContext = context };
        if (context is null || selected.Descriptor?.IsLocal != false) return routed;
        return routed with
        {
            Messages = Array.AsReadOnly(original.Messages.Select(message => message with
            {
                Images = message.Images is null ? null : Array.AsReadOnly(message.Images.ToArray()),
                ToolCalls = message.ToolCalls is null ? null : Array.AsReadOnly(message.ToolCalls.Select(call => call with
                { Arguments = new System.Collections.ObjectModel.ReadOnlyDictionary<string, System.Text.Json.JsonElement>(
                    call.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)) }).ToArray())
            }).ToArray()),
            Tools = Array.AsReadOnly(original.Tools.Select(tool => tool with
            {
                Properties = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(
                    tool.Properties.ToDictionary(pair => pair.Key, pair => (object)System.Text.Json.JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal)),
                Required = Array.AsReadOnly(tool.Required.ToArray()),
                InputSchema = tool.InputSchema is { } schema ? schema.Clone() : null
            }).ToArray())
        };
    }

    private async Task<TaskExecutionSnapshot> RefreshObservedContextAsync(RoutingState state, CancellationToken token)
    {
        var context = state.Context ?? throw new InvalidOperationException("The canonical task observation is unavailable.");
        if (!state.InitialObservationValidated || taskCoordinator is null)
            throw new InvalidOperationException("The initial canonical request observation was not validated.");
        var snapshot = await taskCoordinator.GetAsync(context.TaskId, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original canonical task is unavailable.");
        if (context.TaskId == Guid.Empty || context.ContextId == Guid.Empty || context.ExecutionId == Guid.Empty
            || snapshot.TaskId != context.TaskId || snapshot.ContextId != context.ContextId || snapshot.ExecutionId != context.ExecutionId
            || snapshot.Attempts.LastOrDefault()?.Id != context.AttemptId || snapshot.OwnerBinding != state.OriginalOwner
            || snapshot.PersistenceRevision < context.PersistenceRevision
            || snapshot.State is TaskExecutionLifecycle.Completed or TaskExecutionLifecycle.Cancelled)
            throw new InvalidOperationException("The provider continuation has a stale attempt, changed owner or different canonical task/run.");
        // Initial stale client observations were rejected before any callbacks. Once the SAME request
        // is admitted, queue/steer metadata can legitimately advance during provider I/O or CAS observers.
        // Refreshing this observation grants nothing: the actual issuer/actor/candidate revalidates below.
        state.Context = context with { PersistenceRevision = snapshot.PersistenceRevision };
        return snapshot;
    }

    private TaskRunOriginalFailureObservation? ObserveOriginalFrame(RoutingState state, Task originalFrame) =>
        state.Admission is { } admission ? originalFrames?.TryObserveProviderFailure(admission, originalFrame) : null;

    private static bool CurrentAttemptCoversSelection(TaskRunRouteCandidate left, TaskRunRouteCandidate right) =>
        left.RouteId == right.RouteId && left.RouteRevision == right.RouteRevision
        && left.ProviderId == right.ProviderId && left.ModelId == right.ModelId
        && left.ArtifactIdentity == right.ArtifactIdentity && left.UsesCloud == right.UsesCloud
        && right.RequiredCapabilities.Count != 0
        && right.RequiredCapabilities.All(required => left.RequiredCapabilities.Contains(required, StringComparer.Ordinal));

    private async Task RecordProviderFailureAsync(RoutingState state, Task originalFrame, Exception failure, CancellationToken token,
        bool allowRecovery = true, TaskRunOriginalFailureObservation? originalObservation = null)
    {
        // Caller cancellation belongs to the original execution owner, not quota recovery.
        var admissionProviderId = state.Admission?.Lease.Candidate.ProviderId;
        if (token.IsCancellationRequested || failure is OperationCanceledException && !IsRecoverable(failure, token, admissionProviderId)) return;
        if (state.Context is not { } context || state.Admission is not { } admission || taskCoordinator is null) return;
        var originalFailure = allowRecovery && IsRecoverable(failure, token, admissionProviderId)
            ? (originalFrames ?? throw new InvalidOperationException("The original frame owner is unavailable."))
                .TryObserveProviderFailure(admission, originalFrame)
            : null;
        if (originalFailure is not null && (originalObservation is not null && !ReferenceEquals(originalFailure, originalObservation)
            || !ReferenceEquals(originalFailure.OriginalCause, failure)))
            throw new InvalidOperationException("The actual registered provider frame did not issue this exact cause observation.", failure);
        if (allowRecovery && IsRecoverable(failure, token, admissionProviderId) && originalFailure is null)
            throw new InvalidOperationException("The actual terminal provider frame has no eligible original failure observation.", failure);
        var http = failure is HttpRequestException requestFailure ? (int?)requestFailure.StatusCode : null;
        var verifiedCreditsExhausted = http == 402 && IsRecoverable(failure, token, admissionProviderId);
        var safeFailure = new ExecutionFailure(!allowRecovery ? "PROVIDER_PARTIAL_OUTPUT_REQUIRES_REVIEW" : verifiedCreditsExhausted ? "PROVIDER_CREDITS_EXHAUSTED" : http == 429 ? "PROVIDER_RATE_LIMITED"
                : failure is TaskCanceledException { InnerException: TimeoutException } ? "PROVIDER_TIMEOUT" : "PROVIDER_CALL_FAILED",
            verifiedCreditsExhausted ? "Provider credits exhausted" : http == 429 ? "Provider usage limit reached" : "Provider request failed",
            "The original provider attempt failed. Accepted work and its canonical task/run are retained.",
            HttpStatus: http, AffectedComponent: admission.Lease.Candidate.ProviderId);
        try
        {
            var acknowledged = await taskCoordinator.RecordAttemptFailureAsync(context.TaskId, context.ExecutionId,
                admission.AttemptId, safeFailure, token, originalFailure: originalFailure).ConfigureAwait(false);
            state.Context = context with { PersistenceRevision = acknowledged.PersistenceRevision };
            RecordOriginalAcknowledgedRequestFailure(state, originalFailure);
        }
        catch (Exception observationFailure)
        {
            throw new AggregateException("Provider failure and its canonical failure checkpoint both failed.", failure, observationFailure);
        }
    }

    private async Task<bool> IsPermittedForToolsAsync(ProviderModelDescriptor actual,
        IReadOnlyCollection<RestrictedModelCapability> restrictions, CancellationToken token)
    {
        if (restrictions.Count == 0) return true;
        var evaluator = modelPermissions ?? throw new InvalidOperationException("Actual model tool-governance authority is unavailable.");
        foreach (var restriction in restrictions)
            if (!(await evaluator.EvaluateAsync(actual, restriction, cancellationToken: token).ConfigureAwait(false)).Allowed)
                return false;
        return true;
    }

    private async Task RecordCatalogueUnavailableAsync(RoutingState state, SelectedProvider selected, ProviderCatalogueFailure original, CancellationToken token)
    {
        ClearOriginalRequestFailure(state);
        var context = state.Context ?? throw new InvalidOperationException("A real canonical provider attempt is required.");
        var admission = state.Admission ?? throw new InvalidOperationException("Original attempt custody is unavailable.");
        if (taskCoordinator is null || originalFrames is null)
            throw new InvalidOperationException("Canonical provider-failure checkpoint and settlement are unavailable.");
        // A current Task/Run observation cannot retarget another provider's catalogue failure
        // onto this issued attempt. Only an actual captured/retained descriptor paired with
        // the SAME original lease selection can enter the zero-frame failure checkpoint.
        var actual = selected.Descriptor ?? throw new InvalidOperationException(
            "The original issued selection has no actual retained descriptor for this catalogue failure.", original.Cause);
        var issued = admission.Lease.Candidate;
        if (actual.ProviderId != issued.ProviderId || actual.Name != issued.ModelId || !actual.IsLocal != issued.UsesCloud)
            throw new UnauthorizedAccessException("This catalogue failure does not belong to the original issued provider/model.", original.Cause);
        GuardSelectedProvider(selected);
        if (!IsRecoverable(original.Cause, token, issued.ProviderId)) ExceptionDispatchInfo.Capture(original.Cause).Throw();
        await RefreshObservedContextAsync(state, token).ConfigureAwait(false);
        context = state.Context!;
        var observed = new ExecutionFailure("PROVIDER_CATALOGUE_UNAVAILABLE", "Provider catalogue unavailable",
            "The actual selected provider catalogue failed. Its original attempt and accepted work are retained.",
            HttpStatus: original.Cause is HttpRequestException http ? (int?)http.StatusCode : null,
            AffectedComponent: admission.Lease.Candidate.ProviderId);
        try
        {
            // No raw provider frame exists for this catalogue operation. This issues no body-failure
            // acknowledgment. Registered zero-frame custody and all prior originals still must settle.
            var acknowledged = await taskCoordinator.RecordAttemptFailureAsync(context.TaskId, context.ExecutionId,
                admission.AttemptId, observed, token).ConfigureAwait(false);
            state.Context = context with { PersistenceRevision = acknowledged.PersistenceRevision };
        }
        catch (Exception checkpointFailure)
        {
            throw new AggregateException("Original catalogue failure and canonical checkpoint both failed.", original.Cause, checkpointFailure);
        }
    }

    private void GuardSelectedProvider(SelectedProvider selected)
    {
        if (selected.Descriptor is not { } descriptor) return; // The established raw primary validates legacy unknown keys.
        var provider = providers.Find(descriptor.ProviderId)
            ?? throw new InvalidOperationException("The selected provider is no longer registered.");
        if (provider.IsLocal != descriptor.IsLocal)
            throw new InvalidDataException("The selected provider catalogue identity changed.");
        if (!provider.IsLocal && (privacy.Current.LocalOnlyMode || RuntimeSafetyState.IsSafeMode))
            throw new UnauthorizedAccessException("Cloud providers are disabled by the current local-only or safety policy.");
    }

    private IAsyncEnumerable<string> RawStream(SelectedProvider selected, OllamaChatRequest request, CancellationToken token) =>
        selected.Descriptor is { } actual
            ? providers.GetRequired(actual.ProviderId).StreamChatAsync(request.Model == actual.Name ? request : request with { Model = actual.Name }, token)
            : primary.StreamChatAsync(request, token);

    private Task<string> RawCompleteAsync(SelectedProvider selected, OllamaChatRequest request, CancellationToken token) =>
        selected.Descriptor is { } actual
            ? providers.GetRequired(actual.ProviderId).CompleteAsync(request.Model == actual.Name ? request : request with { Model = actual.Name }, token)
            : primary.CompleteAsync(request, token);

    private Task<OllamaToolResponse> RawToolsAsync(SelectedProvider selected, OllamaToolRequest request, CancellationToken token) =>
        selected.Descriptor is { } actual
            ? providers.GetRequired(actual.ProviderId).ChatWithToolsAsync(request.Model == actual.Name ? request : request with { Model = actual.Name }, token)
            : primary.ChatWithToolsAsync(request, token);

    /// <summary>Records actual admitted recovery in the same canonical Task/Run; standalone calls invent no run.</summary>
    private void PublishFallback(string requestedModel, string actualModel, ProviderExecutionContext? context)
    {
        if (executionEvents is null || context?.AttemptId is not { } attemptId) return;
        executionEvents.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), context.ExecutionId, attemptId, context.ActionId, ExecutionOrigin.Haven,
            ExecutionActionType.ModelFallback, ExecutionActionStatus.Running,
            "Authorized same-run provider recovery admitted", null, null, "model-routing", DateTimeOffset.UtcNow,
            TaskId: context.TaskId, SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["requested"] = requestedModel, ["used"] = actualModel,
                ["attemptId"] = attemptId.ToString("D")
            }));
    }

    private async Task<ProviderCatalogueAvailability> GetEligibleModelsAsync(CancellationToken cancellationToken)
    {
        var models = new List<ProviderModelDescriptor>();
        var failures = new Dictionary<string, ProviderCatalogueFailure>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers.Providers.Where(item => (!privacy.Current.LocalOnlyMode && !RuntimeSafetyState.IsSafeMode) || item.IsLocal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task<IReadOnlyList<ProviderModelDescriptor>>? originalCatalogue = null;
            try
            {
                originalCatalogue = provider.GetModelsAsync(cancellationToken);
                var discovered = await AwaitExactTaskAsync(originalCatalogue).ConfigureAwait(false);
                if (discovered.Any(model => !model.ProviderId.Equals(provider.Id, StringComparison.OrdinalIgnoreCase)
                    || model.IsLocal != provider.IsLocal))
                    throw new InvalidDataException("Provider catalogue identity does not match its registered owner.");
                models.AddRange(discovered);
            }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken, provider.Id))
            {
                failures.Add(provider.Id, new(originalCatalogue, ex));
                // Preserve the original catalogue Task and classified cause, not an invented model.
            }
        }

        return new(models.GroupBy(model => model.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray(), failures);
    }

    /// <summary>
    /// Performs the required capabilities step owned by this component.
    /// </summary>
    private static IReadOnlySet<ToolCapability> RequiredCapabilities(OllamaChatRequest request)
    {
        var required = new HashSet<ToolCapability> { ToolCapability.Text };
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) required.Add(ToolCapability.Vision);
        return required;
    }

    /// <summary>
    /// Performs the required capabilities step owned by this component.
    /// </summary>
    private static IReadOnlySet<ToolCapability> RequiredCapabilities(OllamaToolRequest request)
    {
        var required = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools };
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) required.Add(ToolCapability.Vision);
        return required;
    }

    /// <summary>
    /// Reports whether recoverable applies to the current state.
    /// </summary>
    private bool IsRecoverable(Exception exception, CancellationToken cancellationToken, string? issuedProviderId = null) => !cancellationToken.IsCancellationRequested && (exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        TaskCanceledException { InnerException: TimeoutException } when !cancellationToken.IsCancellationRequested => true,
        HttpRequestException { StatusCode: null } => true,
        // Official OpenRouter contract: 402 is account/API-key credit exhaustion. This
        // requires the actually issued provider identity/kind; arbitrary HTTP402,401,403
        // remain ineligible. It grants no fallback route or permission on its own.
        HttpRequestException { StatusCode: HttpStatusCode.PaymentRequired }
            when issuedProviderId == "openrouter" && providers.Find(issuedProviderId) is { Kind: ModelProviderKind.OpenRouter, IsLocal: false } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
        HttpRequestException { StatusCode: HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
        _ => false
    });

    /// <summary>
    /// Performs the provider id step owned by this component.
    /// </summary>
    private static string ProviderId(string model)
    {
        var separator = model.IndexOf(':');
        if (separator <= 0) return "ollama";
        var prefix = model[..separator];
        return prefix is "openai" or "anthropic" or "gemini" or "openrouter" or "openai-compatible" or "ollama" or "llama-cpp" ? prefix : "ollama";
    }

    /// <summary>
    /// Performs the model name step owned by this component.
    /// </summary>
    private static string ModelName(string model)
    {
        var provider = ProviderId(model);
        return provider == "ollama" || !model.StartsWith(provider + ":", StringComparison.OrdinalIgnoreCase)
            ? model
            : model[(provider.Length + 1)..];
    }
}
