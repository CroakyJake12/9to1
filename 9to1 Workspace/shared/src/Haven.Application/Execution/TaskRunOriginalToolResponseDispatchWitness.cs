namespace Haven.Application;

/// <summary>The inspected finite methods contain no independent Haven-native/app tool
/// dispatch. This says nothing about model billing, network, server tools or other effects.</summary>
public enum TaskRunOriginalToolResponseDispatchScope
{
    NoIndependentHavenNativeDispatchInInspectedMethod = 1,
    NoIndependentHavenNativeDispatchInEveryInspectedInvocation = 2
}

/// <summary>Opaque member of the complete actually invoked finite tool-response cohort.
/// Raw failures remain failures; successful settlement conveys no effect knowledge.</summary>
public interface TaskRunOriginalToolResponseDispatchInvocation
{
    IModelProvider OriginalSelectedProvider { get; }
    OllamaToolRequest OriginalRoutedRequest { get; }
    Task<OllamaToolResponse> OriginalProviderTask { get; }
    Task OriginalProviderFrame { get; }
    TaskRunOriginalIssuedRouteConfiguration OriginalIssuedRouteConfiguration { get; }
    TaskRunOriginalFailedAttemptSettlement OriginalFailedAttempt { get; }
    string InspectedMethod { get; }
    TaskRunOriginalToolResponseDispatchScope Scope { get; }
    TaskRunOriginalFailureEffectKnowledge OtherProviderEffects { get; }
}

/// <summary>Opaque observation issued only by the SAME configured router after EVERY
/// actual raw invocation in an authentic final failed tool-response call is inspected and
/// settled. Public fields alone cannot establish complete history or private issuance.</summary>
public interface TaskRunOriginalToolResponseDispatchWitness
{
    TaskRunOriginalFinalRequestFailure OriginalFailure { get; }
    IReadOnlyList<TaskRunOriginalToolResponseDispatchInvocation> OriginalInvocations { get; }
    // The final invocation remains directly available; these fields do not describe earlier calls.
    IModelProvider OriginalSelectedProvider { get; }
    OllamaToolRequest OriginalRoutedRequest { get; }
    Task<OllamaToolResponse> OriginalProviderTask { get; }
    Task OriginalProviderFrame { get; }
    TaskRunOriginalIssuedRouteConfiguration OriginalIssuedRouteConfiguration { get; }
    string InspectedMethod { get; }
    TaskRunOriginalToolResponseDispatchScope Scope { get; }
    TaskRunOriginalFailureEffectKnowledge OtherProviderEffects { get; }
}

public interface ITaskRunOriginalToolResponseDispatchWitnessSource
{
    TaskRunOriginalToolResponseDispatchWitness? TryGetOriginalToolResponseDispatchWitness(
        TaskRunOriginalFinalRequestFailure sameFailure);
    bool IsIssuedOriginalToolResponseDispatchWitness(TaskRunOriginalToolResponseDispatchWitness witness,
        TaskRunOriginalFinalRequestFailure sameFailure);
}
