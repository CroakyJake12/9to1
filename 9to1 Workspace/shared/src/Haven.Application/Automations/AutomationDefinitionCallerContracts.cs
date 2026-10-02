using Haven.Core;
namespace Haven.Application.Automations;

/// <summary>Original host-issued selection, never a serialized or model supplied execution grant.</summary>
public interface IAutomationDefinitionCallerSelection
{
    Guid StoreId { get; }
    AuthenticatedResourceActor Actor { get; }
}
public sealed record AutomationDefinitionCallerOutcome(bool? Committed, string Code);
public interface IAutomationDefinitionCallerReview
{
    Guid OperationId { get; }
    string RequestId { get; }
    Task<AutomationDefinitionCallerOutcome> FinishAsync(CancellationToken cancellationToken = default);
}
public sealed record AutomationDefinitionCallerLibrary(AutomationLibraryPage<AutomationDefinition> Definitions,
    AutomationLibraryPage<ReusableTaskDefinition> Tasks);
public interface IAutomationDefinitionReviewCaller
{
    Task RequireCurrentAsync(IAutomationDefinitionCallerSelection selection, CancellationToken cancellationToken = default);
    Task<AutomationDefinitionCallerLibrary> LoadLibraryAsync(IAutomationDefinitionCallerSelection selection,
        AutomationLibraryQuery query, CancellationToken cancellationToken = default);
    Task<IAutomationDefinitionCallerSelection> CaptureAsync(AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken = default);
    Task<IAutomationDefinitionCallerReview> ReviewAsync(IAutomationDefinitionCallerSelection selection,
        AutomationDefinition proposal, long expectedRevision, AutomationDefinitionChangeKind kind, CancellationToken cancellationToken = default);
    Task<IAutomationDefinitionCallerReview> ReviewAsync(IAutomationDefinitionCallerSelection selection,
        ReusableTaskDefinition proposal, long expectedRevision, AutomationDefinitionChangeKind kind, CancellationToken cancellationToken = default);
}
