using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.MiniComputer;

public enum AssistantMiniComputerImportAction { Request, Refresh, Complete, RetryAudit }
public sealed class AssistantMiniComputerImportPreview
{
    internal AssistantMiniComputerImportPreview(object issuer, AssistantConversationBinding binding, object? original,
        string state, string reason, string? requestId, bool canRequest, bool canComplete, bool canRetryAudit, bool canBrowse)
    { Issuer = issuer; Binding = binding; Original = original; State = state; Reason = reason; RequestId = requestId;
      CanRequest = canRequest; CanComplete = canComplete; CanRetryAudit = canRetryAudit; CanBrowse = canBrowse; }
    internal object Issuer { get; }
    internal AssistantConversationBinding Binding { get; }
    internal object? Original { get; }
    public string State { get; }
    public string Reason { get; }
    public string? RequestId { get; }
    public bool CanRequest { get; }
    public bool CanComplete { get; }
    public bool CanRetryAudit { get; }
    public bool CanBrowse { get; }
}
public interface IAssistantMiniComputerImportController
{
    Task<AssistantMiniComputerImportPreview> InspectImportAsync(AssistantConversationBinding binding, CancellationToken token = default);
    Task<AssistantMiniComputerImportPreview> ImportAsync(AssistantMiniComputerImportPreview samePreview,
        AssistantMiniComputerImportAction action, CancellationToken token = default);
}
