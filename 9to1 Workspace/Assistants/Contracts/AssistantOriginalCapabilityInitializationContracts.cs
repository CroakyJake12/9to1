using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Assistants.Contracts;

public sealed class AssistantOriginalCapabilityInitializationIntent
{
    internal AssistantOriginalCapabilityInitializationIntent(object issuer, object original,
        AssistantDefinitionSnapshot definition, AuthenticatedResourceActor actor,
        Guid operationId, IReadOnlyList<CapabilityDefinition> missingDefinitions, bool recovered)
    { Issuer = issuer; Original = original; Definition = definition; Actor = actor; OperationId = operationId;
        MissingDefinitions = missingDefinitions; IsRecoveredOperation = recovered; }
    internal object Issuer { get; }
    internal object Original { get; }
    public AssistantDefinitionSnapshot Definition { get; }
    public AuthenticatedResourceActor Actor { get; }
    public Guid OperationId { get; }
    public IReadOnlyList<CapabilityDefinition> MissingDefinitions { get; }
    public bool IsRecoveredOperation { get; }
}

public sealed class AssistantOriginalCapabilityInitializationCompletion
{
    internal AssistantOriginalCapabilityInitializationCompletion(AssistantOriginalCapabilityInitializationObservation observation,
        CapabilityOriginalInitializationCompletionKind kind, IReadOnlyList<Guid> insertedIds, bool recovered)
    { OriginalObservation = observation; Kind = kind; InsertedDefinitionIds = insertedIds; IsRecoveredOperation = recovered; }
    public AssistantOriginalCapabilityInitializationObservation OriginalObservation { get; }
    public CapabilityOriginalInitializationCompletionKind Kind { get; }
    public IReadOnlyList<Guid> InsertedDefinitionIds { get; }
    public bool IsRecoveredOperation { get; }
}

/// <summary>Source-bound process setup observation. View retirement detaches this delivery;
/// it does not retire the actual SQL/Home operation. Results remain metadata, not tool grants.</summary>
public sealed class AssistantOriginalCapabilityInitializationObservation : IAsyncDisposable
{
    internal AssistantOriginalCapabilityInitializationObservation(object issuer, object original,
        AssistantOriginalCapabilityInitializationIntent intent,
        Func<CancellationToken, Task<AssistantOriginalCapabilityInitializationCompletion>> wait,
        Action requestRetirement, Action demandJoin, Func<Task> close, Func<Task?> peekClose)
    { Issuer = issuer; Original = original; OriginalIntent = intent; _wait = wait; _request = requestRetirement;
        _demand = demandJoin; _close = close; _peek = peekClose; }
    internal object Issuer { get; }
    internal object Original { get; }
    public AssistantOriginalCapabilityInitializationIntent OriginalIntent { get; }
    private readonly Func<CancellationToken, Task<AssistantOriginalCapabilityInitializationCompletion>> _wait;
    private readonly Action _request, _demand;
    private readonly Func<Task> _close;
    private readonly Func<Task?> _peek;
    public Task? OriginalClose => _peek();
    public Task<AssistantOriginalCapabilityInitializationCompletion> WaitOriginalCompletionAsync(CancellationToken token = default) => _wait(token);
    public void RequestOriginalRetirement() => _request();
    public void DemandExternalOriginalJoin() => _demand();
    public Task CloseAndDrainOriginalAsync() => _close();
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}

public interface IAssistantOriginalCapabilityInitializationOwner
{
    Task<AssistantOriginalCapabilityInitializationIntent> PrepareOriginalConfigurationCapabilityInitializationWithinSourceAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, Guid operationId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalConfigurationInitializationIntent(AssistantOriginalCapabilityInitializationIntent sameActual);
    Task RevalidateOriginalConfigurationInitializationIntentWithinSourceAsync(AssistantOriginalCapabilityInitializationIntent sameActual,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task<AssistantOriginalCapabilityInitializationObservation> StartOriginalConfigurationCapabilityInitializationWithinSourceAsync(
        AssistantOriginalCapabilityInitializationIntent sameActual, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalConfigurationInitializationObservation(AssistantOriginalCapabilityInitializationObservation sameActual);
}
