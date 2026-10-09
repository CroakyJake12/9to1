using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed class AssistantMiniComputerIdentitySetupPreview
{
    internal AssistantMiniComputerIdentitySetupPreview(object issuer, AssistantConversationBinding binding,
        ICanonicalMiniComputerCatalogIdentityIntent? intent, string reason)
    { Issuer = issuer; Binding = binding; Intent = intent; Reason = reason; }
    internal object Issuer { get; }
    internal AssistantConversationBinding Binding { get; }
    internal ICanonicalMiniComputerCatalogIdentityIntent? Intent { get; }
    public bool CanRequest => Intent is not null;
    public string Reason { get; }
    public string CatalogueName => Intent?.CatalogueName ?? "Configured Mini Computer catalogue";
    public long? OriginalByteLength => Intent?.OriginalByteLength;
}
public sealed record AssistantMiniComputerIdentitySetupResult(bool Applied, string Reason);
public interface IAssistantMiniComputerIdentitySetupController
{
    Task<AssistantMiniComputerIdentitySetupPreview> PrepareIdentitySetupAsync(AssistantConversationBinding sameBinding,
        Guid operationId, CancellationToken token = default);
    Task<AssistantMiniComputerIdentitySetupResult> ExecuteIdentitySetupAsync(AssistantMiniComputerIdentitySetupPreview samePreview,
        CancellationToken token = default);
    AssistantMiniComputerPendingObservation ObserveOriginalIdentitySetup(AssistantMiniComputerIdentitySetupPreview samePreview);
}
