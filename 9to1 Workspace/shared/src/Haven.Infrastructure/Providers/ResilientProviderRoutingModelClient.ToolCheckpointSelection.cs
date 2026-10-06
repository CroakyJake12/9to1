using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class ResilientProviderRoutingModelClient
{
    private readonly ConditionalWeakTable<TaskRunOriginalToolCheckpointSelection, OriginalCheckpointSelectionBody> _issuedCheckpointSelections = new();

    public Task<TaskRunOriginalToolCheckpointSelection?> SelectLocalForOriginalToolCheckpointAsync(
        TaskRunOriginalFinalRequestFailure sameFailure, TaskExecutionSnapshot actualCurrentSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sameFailure);
        ArgumentNullException.ThrowIfNull(actualCurrentSnapshot);
        if (sameFailure.OriginalCallerRequest is not OllamaToolRequest request
            || !IsIssuedOriginalFinalRequestFailure(sameFailure, request, sameFailure.OriginalOutwardFailure))
            throw new UnauthorizedAccessException("No SAME final failed tool-response call was issued by this router.");
        lock (_originalRequestFailureSync)
        {
            var failure = GetOriginalTerminalFailure(request, sameFailure.OriginalAdmission, sameFailure.OriginalOutwardFailure)
                ?? throw new UnauthorizedAccessException("The original final call is no longer current.");
            if (failure.CheckpointSelections.Count >= 64)
                throw new InvalidOperationException("Original checkpoint selection custody is full; no new callback is admitted.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var selection = new OriginalCheckpointSelectionBody(failure, sameFailure, actualCurrentSnapshot);
            // Publish the actual whole driver before any catalogue/actor/governance source callback.
            selection.Whole = SelectOriginalLocalCheckpointBodyAsync(selection, cancellationToken, start.Task);
            failure.CheckpointSelections.Add(selection);
            start.SetResult();
            return selection.Whole;
        }
    }

    private async Task<TaskRunOriginalToolCheckpointSelection?> SelectOriginalLocalCheckpointBodyAsync(
        OriginalCheckpointSelectionBody selection, CancellationToken token, Task start)
    {
        await start.ConfigureAwait(false);
        try
        {
            var failure = selection.Failure;
            var binding = selection.Binding;
            if (taskCoordinator is null || routeCapture is null || catalogueEligibility is null
                || !IsIssuedOriginalFinalRequestFailure(binding, failure.Request, binding.OriginalOutwardFailure)) return null;
            if (TryGetOriginalRequestFailure((OllamaToolRequest)failure.Request, binding.OriginalAdmission,
                binding.OriginalOutwardFailure) is null) return null; // No settlement is started by this lookup.
            var expected = selection.Expected;
            var current = await ObserveCheckpointOriginalAsync(selection,
                () => taskCoordinator.GetAsync(binding.OriginalAdmission.Snapshot.TaskId, token)).ConfigureAwait(false);
            if (current is null || current.TaskId != binding.OriginalAdmission.Snapshot.TaskId
                || current.ContextId != binding.OriginalAdmission.Snapshot.ContextId
                || current.ExecutionId != binding.OriginalAdmission.Snapshot.ExecutionId
                || current.OwnerBinding != binding.OriginalAdmission.Snapshot.OwnerBinding
                || current.State != TaskExecutionLifecycle.Suspended
                || current.Attempts.LastOrDefault()?.Id != binding.OriginalAdmission.AttemptId
                || SnapshotFingerprint(current) != SnapshotFingerprint(expected))
                throw new UnauthorizedAccessException("The supplied checkpoint is not the actual current SAME failed task/run row.");
            selection.Current = current;
            selection.SnapshotDigest = SnapshotFingerprint(current);

            // A declared IsLocal/localhost/custom provider is not enough. Only the actual private
            // AF_UNIX llama.cpp producer and its observed Text/Tools catalogue can enter this port.
            var actualLocalOwners = providers.Providers.OfType<LlamaCppModelProvider>().ToArray();
            foreach (var local in actualLocalOwners)
            {
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(providers.Find(local.Id), local)) continue;
                var catalogue = await ObserveCheckpointOriginalAsync(selection,
                    () => local.GetModelsAsync(token)).ConfigureAwait(false);
                var compatible = catalogue.Where(model => model.ProviderId == local.Id && model.IsLocal
                    && failure.Required.All(model.Supports)).ToArray();
                var eligible = catalogueEligibility.ObserveOriginalCatalogueEligibility(compatible,
                    failure.Required.ToHashSet(), new ModelRoutingPolicy(ModelRoutingMode.ManualFallback,
                        PreferLocal: true, AllowCloud: false, PreferredModelKeys: compatible.Select(model => model.Key).ToArray(), AllowFallback: false));
                foreach (var model in compatible.Where(model => eligible.Any(original => ReferenceEquals(original, model))))
                {
                    if (failure.Restrictions.Length != 0 && modelPermissions is null) return null;
                    var allowed = true;
                    foreach (var restriction in failure.Restrictions)
                    {
                        var decision = await ObserveCheckpointOriginalAsync(selection,
                            () => modelPermissions!.EvaluateAsync(model, restriction, cancellationToken: token)).ConfigureAwait(false);
                        if (!decision.Allowed) { allowed = false; break; }
                    }
                    if (!allowed) continue;
                    var candidate = await ObserveCheckpointOriginalAsync(selection,
                        () => routeCapture.CaptureSelectedRouteAsync(current, model, failure.Required,
                            failure.Restrictions, token)).ConfigureAwait(false);
                    if (candidate.UsesCloud || candidate.ProviderId != model.ProviderId || candidate.ModelId != model.Name
                        || !failure.Required.Select(value => value.ToString()).Order(StringComparer.Ordinal)
                            .SequenceEqual(candidate.RequiredCapabilities.Order(StringComparer.Ordinal)))
                        throw new UnauthorizedAccessException("The actual issuing selector did not capture the SAME local capability selection.");
                    var observation = await ObserveCheckpointOriginalAsync(selection,
                        () => local.ObserveOriginalEndpointAsync(token)).ConfigureAwait(false);
                    if (!local.IsOriginalEndpointObservation(observation) || observation.ProviderId != model.ProviderId
                        || observation.ModelId != model.Name || !failure.Required.All(observation.ObservedCapabilities.Contains)) continue;
                    var latest = await ObserveCheckpointOriginalAsync(selection,
                        () => taskCoordinator.GetAsync(current.TaskId, token)).ConfigureAwait(false);
                    if (latest is null || SnapshotFingerprint(latest) != selection.SnapshotDigest
                        || !IsIssuedOriginalFinalRequestFailure(binding, failure.Request, binding.OriginalOutwardFailure)
                        || !ReferenceEquals(providers.Find(local.Id), local) || !local.IsOriginalEndpointObservation(observation))
                        throw new UnauthorizedAccessException("The checkpoint or observed local owner changed during actual route capture.");
                    selection.Local = local;
                    selection.Observation = observation;
                    selection.Model = model;
                    selection.Candidate = candidate;
                    var issued = new OriginalToolCheckpointSelection(selection);
                    lock (_originalRequestFailureSync) _issuedCheckpointSelections.Add(issued, selection);
                    return issued;
                }
            }
            return null;
        }
        catch (OperationCanceledException cause)
        {
            // Preserve a synchronous/faulted source OCE as fault evidence. True returned canceled
            // sources are a separate outcome and never produce a selection receipt either.
            if (selection.SynchronousCauses.Any(error => ReferenceEquals(error, cause))
                || selection.Sources.Any(original => original.IsFaulted
                    && original.Exception!.InnerExceptions.Any(error => ReferenceEquals(error, cause))))
                throw new AggregateException("The actual checkpoint selection source faulted with an OCE.", cause);
            throw;
        }
    }

    public bool IsIssuedOriginalToolCheckpointSelection(TaskRunOriginalToolCheckpointSelection selection,
        TaskRunOriginalFinalRequestFailure sameFailure, TaskExecutionSnapshot actualCurrentSnapshot)
    {
        if (selection is null || sameFailure is null || actualCurrentSnapshot is null) return false;
        OriginalCheckpointSelectionBody? body;
        lock (_originalRequestFailureSync)
            if (!_issuedCheckpointSelections.TryGetValue(selection, out body)
                || !ReferenceEquals(body.Binding, sameFailure) || body.Whole is not { IsCompletedSuccessfully: true }
                || body.Sources.Any(source => !source.IsCompletedSuccessfully)) return false;
        // Never hold the router gate across other owner/source validation or serialization.
        return body.Current is not null && body.Local is not null && body.Observation is not null
            && ReferenceEquals(selection.OriginalSelectionTask, body.Whole)
            && ReferenceEquals(selection.ActualCurrentSnapshot, body.Current)
            && ReferenceEquals(selection.ActualSelectedModel, body.Model)
            && ReferenceEquals(selection.ActualSelectedCandidate, body.Candidate)
            && ReferenceEquals(selection.OriginalLocalEndpointObservation, body.Observation)
            && SnapshotFingerprint(actualCurrentSnapshot) == body.SnapshotDigest
            && SnapshotFingerprint(body.Current) == body.SnapshotDigest
            && IsIssuedOriginalFinalRequestFailure(sameFailure, body.Failure.Request, sameFailure.OriginalOutwardFailure)
            && ReferenceEquals(providers.Find(body.Local.Id), body.Local)
            && body.Local.IsOriginalEndpointObservation(body.Observation);
    }

    private async Task<T> ObserveCheckpointOriginalAsync<T>(OriginalCheckpointSelectionBody body, Func<Task<T>> acquire)
    {
        if (body.Sources.Count >= 512) throw new InvalidOperationException("Original checkpoint source history is full; no further callback is admitted.");
        Task<T> actual;
        try { actual = acquire() ?? throw new InvalidOperationException("An actual checkpoint source returned no Task."); }
        catch (Exception cause) { body.SynchronousCauses.Add(cause); throw; }
        body.Sources.Add(actual);
        return await AwaitExactTaskAsync(actual).ConfigureAwait(false);
    }
    private static string SnapshotFingerprint(TaskExecutionSnapshot snapshot) => JsonSerializer.Serialize(snapshot);
    private sealed class OriginalCheckpointSelectionBody(OriginalRequestFailureBody failure,
        TaskRunOriginalFinalRequestFailure binding, TaskExecutionSnapshot expected)
    {
        public readonly OriginalRequestFailureBody Failure = failure;
        public readonly TaskRunOriginalFinalRequestFailure Binding = binding;
        public readonly TaskExecutionSnapshot Expected = expected;
        public readonly List<Task> Sources = [];
        public readonly List<Exception> SynchronousCauses = [];
        public Task<TaskRunOriginalToolCheckpointSelection?>? Whole;
        public TaskExecutionSnapshot? Current;
        public string? SnapshotDigest;
        public LlamaCppModelProvider? Local;
        public LocalModelEndpointObservation? Observation;
        public ProviderModelDescriptor? Model;
        public TaskRunRouteCandidate? Candidate;
    }
    private sealed class OriginalToolCheckpointSelection(OriginalCheckpointSelectionBody body) : TaskRunOriginalToolCheckpointSelection
    {
        public TaskRunOriginalFinalRequestFailure OriginalFailure => body.Binding;
        public Task OriginalSelectionTask => body.Whole!;
        public IReadOnlyList<Task> OriginalSelectionSources { get; } = Array.AsReadOnly(body.Sources.ToArray());
        public TaskExecutionSnapshot ActualCurrentSnapshot => body.Current!;
        public ProviderModelDescriptor ActualSelectedModel => body.Model!;
        public TaskRunRouteCandidate ActualSelectedCandidate => body.Candidate!;
        public LocalModelEndpointObservation OriginalLocalEndpointObservation => body.Observation!;
    }
}
