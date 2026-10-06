using System.Runtime.CompilerServices;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class ResilientProviderRoutingModelClient
{
    private const int OriginalToolDispatchCohortLimit = 128;
    private readonly ConditionalWeakTable<TaskRunOriginalToolResponseDispatchWitness,
        OriginalToolDispatchWitnessBody> _issuedToolDispatchWitnesses = new();

    // The existing fence invokes the actual provider factory once. History refusal must
    // never change the original call or fallback behavior; it only denies this observation.
    private Task<OllamaToolResponse> RawToolsWithOriginalDispatchAsync(RoutingState state,
        SelectedProvider selected, OllamaToolRequest request, CancellationToken token)
    {
        if (selected.Descriptor is not { } actual)
        {
            if (state.OriginalRequestFailure is { } unknown)
                lock (_originalRequestFailureSync) unknown.ToolDispatchCohortUnknown = true;
            return primary.ChatWithToolsAsync(request, token);
        }
        var provider = providers.GetRequired(actual.ProviderId);
        var routed = request.Model == actual.Name ? request : request with { Model = actual.Name };
        var invocation = new OriginalToolDispatchInvocation(provider, routed, state.Admission,
            InspectedOriginalToolResponseMethod(provider));
        if (state.OriginalRequestFailure is { } body)
            lock (_originalRequestFailureSync)
            {
                invocation.Frame = body.ToolDispatchFrame;
                body.ToolDispatch = invocation;
                if (body.ToolDispatchInvocations.Count < OriginalToolDispatchCohortLimit)
                    body.ToolDispatchInvocations.Add(invocation);
                else body.ToolDispatchCohortUnknown = true;
            }

        // Captured-route custody is not a later wire-configuration read or permission grant.
        if (invocation.Method is not null && invocation.Admission is { } admission
            && routeCapture is TaskRunPermissionAuthority
            && routeCapture is ITaskRunOriginalIssuedRouteConfigurationSource configurationSource)
        {
            try { invocation.Configuration = configurationSource.TryObserveOriginalIssuedRouteConfiguration(admission); }
            catch (Exception cause) { invocation.ConfigurationAcquisitionFailure = cause; }
        }
        try { return invocation.Task = provider.ChatWithToolsAsync(routed, token); }
        catch (Exception cause) { invocation.SynchronousProviderFailure = cause; throw; }
    }

    private void BindOriginalToolDispatchFrame(RoutingState state, Task<OllamaToolResponse> originalFrame)
    {
        if (state.OriginalRequestFailure is not { } body) return;
        lock (_originalRequestFailureSync)
        {
            body.ToolDispatchFrame = originalFrame;
            if (body.ToolDispatch is { } invocation && ReferenceEquals(invocation.Admission, state.Admission))
                invocation.Frame = originalFrame;
        }
    }

    private static string? InspectedOriginalToolResponseMethod(IModelProvider provider) => provider switch
    {
        OpenAiModelProvider => "OpenAiCompatibleModelProviderBase.ChatWithToolsAsync/OpenAiModelProvider",
        OpenRouterModelProvider => "OpenAiCompatibleModelProviderBase.ChatWithToolsAsync/OpenRouterModelProvider",
        CustomOpenAiCompatibleModelProvider => "OpenAiCompatibleModelProviderBase.ChatWithToolsAsync/CustomOpenAiCompatibleModelProvider",
        AnthropicModelProvider => "AnthropicModelProvider.ChatWithToolsAsync",
        GeminiModelProvider => "GeminiModelProvider.ChatWithToolsAsync",
        LlamaCppModelProvider => "LlamaCppModelProvider.ChatWithToolsAsync",
        // Mesh and arbitrary implementations/delegates remain unknown even with these IDs.
        _ => null
    };

    public TaskRunOriginalToolResponseDispatchWitness? TryGetOriginalToolResponseDispatchWitness(
        TaskRunOriginalFinalRequestFailure sameFailure)
    {
        if (sameFailure is null) return null;
        OriginalRequestFailureBody body;
        OriginalToolDispatchInvocation[] cohort;
        lock (_originalRequestFailureSync)
        {
            if (!_issuedOriginalFinalFailures.TryGetValue(sameFailure, out body!)
                || body.Request is not OllamaToolRequest || body.Outward is null
                || !IsOriginalToolDispatchCohortCurrent(body, sameFailure)) return null;
            cohort = body.ToolDispatchInvocations.ToArray();
        }
        var settled = ObserveOriginalToolDispatchCohort(body, cohort, sameFailure);
        if (settled is null) return null;
        lock (_originalRequestFailureSync)
        {
            if (!IsOriginalToolDispatchCohortCurrent(body, sameFailure, cohort)) return null;
            var witness = new OriginalToolDispatchWitness(sameFailure, cohort, settled);
            _issuedToolDispatchWitnesses.Add(witness, new(body, cohort, settled, sameFailure));
            return witness;
        }
    }

    public bool IsIssuedOriginalToolResponseDispatchWitness(TaskRunOriginalToolResponseDispatchWitness witness,
        TaskRunOriginalFinalRequestFailure sameFailure)
    {
        if (witness is null || sameFailure is null) return false;
        OriginalToolDispatchWitnessBody issued;
        lock (_originalRequestFailureSync)
        {
            if (!_issuedToolDispatchWitnesses.TryGetValue(witness, out issued!)
                || !ReferenceEquals(issued.Binding, sameFailure)
                || !IsOriginalToolDispatchCohortCurrent(issued.Owner, sameFailure, issued.Invocations)) return false;
        }
        if (!ValidateOriginalToolDispatchCohort(issued.Owner, issued.Invocations,
            issued.Settlements, sameFailure)) return false;
        lock (_originalRequestFailureSync)
            return IsOriginalToolDispatchCohortCurrent(issued.Owner, sameFailure, issued.Invocations);
    }

    // Called only under the router gate. Completion/refusal of any earlier invocation cannot
    // be inferred from the final observation, a public provider name, or registry absence.
    private bool IsOriginalToolDispatchCohortCurrent(OriginalRequestFailureBody body,
        TaskRunOriginalFinalRequestFailure binding, OriginalToolDispatchInvocation[]? expected = null)
    {
        if (body.ToolDispatchCohortUnknown || body.ToolDispatchInvocations.Count == 0
            || body.ToolDispatch is not { } final || body.Outward is null
            || !ReferenceEquals(GetOriginalTerminalFailure(body.Request, body.Outward), body)
            || !ReferenceEquals(body.ToolDispatchInvocations[^1], final)
            || !ReferenceEquals(final.Admission, binding.OriginalAdmission)
            || !ReferenceEquals(final.Observation, binding.OriginalObservation)
            || !ReferenceEquals(final.Frame, binding.OriginalObservation.OriginalFrame)) return false;
        if (expected is null) return true;
        return expected.Length == body.ToolDispatchInvocations.Count
            && expected.Where((invocation, index) => !ReferenceEquals(invocation, body.ToolDispatchInvocations[index])).Any() == false;
    }

    private TaskRunOriginalFailedAttemptSettlement[]? ObserveOriginalToolDispatchCohort(
        OriginalRequestFailureBody body, OriginalToolDispatchInvocation[] cohort,
        TaskRunOriginalFinalRequestFailure binding)
    {
        if (originalFrames is not ITaskRunOriginalFailedAttemptSettlementSource source) return null;
        var settlements = new TaskRunOriginalFailedAttemptSettlement[cohort.Length];
        for (var index = 0; index < cohort.Length; index++)
        {
            var invocation = cohort[index];
            if (!ValidateOriginalToolDispatch(body, invocation, binding)) return null;
            var settled = source.TryGetOriginalFailedAttemptSettlement(invocation.Observation!, invocation.Admission!);
            if (settled is null) return null;
            settlements[index] = settled;
        }
        return ValidateOriginalToolDispatchCohort(body, cohort, settlements, binding) ? settlements : null;
    }

    private bool ValidateOriginalToolDispatchCohort(OriginalRequestFailureBody body,
        OriginalToolDispatchInvocation[] cohort, TaskRunOriginalFailedAttemptSettlement[] settlements,
        TaskRunOriginalFinalRequestFailure binding)
    {
        if (cohort.Length == 0 || cohort.Length != settlements.Length
            || originalFrames is not ITaskRunOriginalFailedAttemptSettlementSource source) return false;
        for (var index = 0; index < cohort.Length; index++)
        {
            var invocation = cohort[index];
            if (!ValidateOriginalToolDispatch(body, invocation, binding)
                || !source.IsIssuedOriginalFailedAttemptSettlement(settlements[index],
                    invocation.Observation!, invocation.Admission!)) return false;
        }
        return true;
    }

    private bool ValidateOriginalToolDispatch(OriginalRequestFailureBody body,
        OriginalToolDispatchInvocation invocation, TaskRunOriginalFinalRequestFailure binding)
    {
        // No router gate is held across the actual authority/frame/registry owner calls.
        if (body.Outward is null || !IsIssuedOriginalFinalRequestFailure(binding, body.Request, body.Outward)
            || invocation.Method is null || invocation.ConfigurationAcquisitionFailure is not null
            || invocation.SynchronousProviderFailure is not null || invocation.Configuration is null
            || invocation.Admission is not { } admission || invocation.Observation is not { } observation
            || invocation.Task is not { IsCompleted: true, IsCompletedSuccessfully: false }
            || invocation.Frame is not { IsCompleted: true, IsCompletedSuccessfully: false } frame
            || !ReferenceEquals(frame, observation.OriginalFrame)
            || !ReferenceEquals(providers.Find(invocation.Provider.Id), invocation.Provider)
            || InspectedOriginalToolResponseMethod(invocation.Provider) != invocation.Method
            || invocation.Provider.Id != admission.Lease.Candidate.ProviderId
            || invocation.Request.Model != admission.Lease.Candidate.ModelId
            || originalFrames is null || !originalFrames.ValidateProviderFailureObservation(observation, admission)
            || routeCapture is not TaskRunPermissionAuthority
            || routeCapture is not ITaskRunOriginalIssuedRouteConfigurationSource source
            || !source.IsIssuedOriginalRouteConfiguration(invocation.Configuration, admission)) return false;
        // Canceled tasks here require the authentic actual failed-frame observation and
        // independently successful settlement, never an OCE/type/token classification.
        return true;
    }

    private sealed class OriginalToolDispatchInvocation(IModelProvider provider, OllamaToolRequest request,
        TaskRunAttemptAdmission? admission, string? method)
    {
        public readonly IModelProvider Provider = provider;
        public readonly OllamaToolRequest Request = request;
        public readonly TaskRunAttemptAdmission? Admission = admission;
        public readonly string? Method = method;
        public TaskRunOriginalIssuedRouteConfiguration? Configuration;
        public Exception? ConfigurationAcquisitionFailure, SynchronousProviderFailure;
        public Task<OllamaToolResponse>? Task;
        public Task<OllamaToolResponse>? Frame;
        public TaskRunOriginalFailureObservation? Observation;
    }
    private sealed record OriginalToolDispatchWitnessBody(OriginalRequestFailureBody Owner,
        OriginalToolDispatchInvocation[] Invocations, TaskRunOriginalFailedAttemptSettlement[] Settlements,
        TaskRunOriginalFinalRequestFailure Binding);

    private sealed class OriginalToolDispatchInvocationWitness(OriginalToolDispatchInvocation invocation,
        TaskRunOriginalFailedAttemptSettlement settled) : TaskRunOriginalToolResponseDispatchInvocation
    {
        public IModelProvider OriginalSelectedProvider => invocation.Provider;
        public OllamaToolRequest OriginalRoutedRequest => invocation.Request;
        public Task<OllamaToolResponse> OriginalProviderTask => invocation.Task!;
        public Task OriginalProviderFrame => invocation.Frame!;
        public TaskRunOriginalIssuedRouteConfiguration OriginalIssuedRouteConfiguration => invocation.Configuration!;
        public TaskRunOriginalFailedAttemptSettlement OriginalFailedAttempt => settled;
        public string InspectedMethod => invocation.Method!;
        public TaskRunOriginalToolResponseDispatchScope Scope => TaskRunOriginalToolResponseDispatchScope.NoIndependentHavenNativeDispatchInInspectedMethod;
        public TaskRunOriginalFailureEffectKnowledge OtherProviderEffects => TaskRunOriginalFailureEffectKnowledge.Unknown;
    }
    private sealed class OriginalToolDispatchWitness : TaskRunOriginalToolResponseDispatchWitness
    {
        private readonly TaskRunOriginalFinalRequestFailure _binding;
        private readonly IReadOnlyList<TaskRunOriginalToolResponseDispatchInvocation> _cohort;
        public OriginalToolDispatchWitness(TaskRunOriginalFinalRequestFailure binding,
            OriginalToolDispatchInvocation[] invocations, TaskRunOriginalFailedAttemptSettlement[] settled)
        {
            _binding = binding;
            _cohort = Array.AsReadOnly(invocations.Select((invocation, index) =>
                (TaskRunOriginalToolResponseDispatchInvocation)new OriginalToolDispatchInvocationWitness(invocation, settled[index])).ToArray());
        }
        private TaskRunOriginalToolResponseDispatchInvocation Final => _cohort[^1];
        public TaskRunOriginalFinalRequestFailure OriginalFailure => _binding;
        public IReadOnlyList<TaskRunOriginalToolResponseDispatchInvocation> OriginalInvocations => _cohort;
        public IModelProvider OriginalSelectedProvider => Final.OriginalSelectedProvider;
        public OllamaToolRequest OriginalRoutedRequest => Final.OriginalRoutedRequest;
        public Task<OllamaToolResponse> OriginalProviderTask => Final.OriginalProviderTask;
        public Task OriginalProviderFrame => Final.OriginalProviderFrame;
        public TaskRunOriginalIssuedRouteConfiguration OriginalIssuedRouteConfiguration => Final.OriginalIssuedRouteConfiguration;
        public string InspectedMethod => Final.InspectedMethod;
        public TaskRunOriginalToolResponseDispatchScope Scope => TaskRunOriginalToolResponseDispatchScope.NoIndependentHavenNativeDispatchInEveryInspectedInvocation;
        public TaskRunOriginalFailureEffectKnowledge OtherProviderEffects => TaskRunOriginalFailureEffectKnowledge.Unknown;
    }
}
