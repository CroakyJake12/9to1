using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed class AssistantMiniComputerChoice
{
    internal AssistantMiniComputerChoice(object issuer, Guid id, Guid provider, string name,
        string guest, long revision, string lastObservedState)
    { Issuer = issuer; VirtualMachineId = id; ProviderId = provider; Name = name;
      Guest = guest; Revision = revision; LastObservedState = lastObservedState; }
    internal object Issuer { get; }
    public Guid VirtualMachineId { get; }
    public Guid ProviderId { get; }
    public string Name { get; }
    public string Guest { get; }
    public long Revision { get; }
    public string LastObservedState { get; }
}

public sealed class AssistantMiniComputerView
{
    internal AssistantMiniComputerView(object issuer, AssistantConversationBinding binding,
        IReadOnlyList<AssistantMiniComputerChoice> choices, AssistantMiniComputerChoice? selected,
        string reason, bool canBrowse, object? original)
    { Issuer = issuer; Binding = binding; Choices = choices; Selected = selected;
      Reason = reason; CanBrowse = canBrowse; Original = original; }
    internal object Issuer { get; }
    internal object? Original { get; }
    internal AssistantConversationBinding Binding { get; }
    public AssistantDefinitionSnapshot Definition => Binding.Definition;
    public IReadOnlyList<AssistantMiniComputerChoice> Choices { get; }
    public AssistantMiniComputerChoice? Selected { get; }
    public bool CanBrowse { get; }
    public string Reason { get; }
    public bool IsOriginalChoice(AssistantMiniComputerChoice choice) =>
        Choices.Any(row => ReferenceEquals(row, choice));
}

public sealed class AssistantMiniComputerOperationPreview
{
    internal AssistantMiniComputerOperationPreview(object issuer, ICanonicalMiniComputerOperationIntent intent)
    { Issuer = issuer; Intent = intent; }
    internal object Issuer { get; }
    internal ICanonicalMiniComputerOperationIntent Intent { get; }
    public Guid OperationId => Intent.OperationId;
    public CanonicalMiniComputerAction Action => Intent.Action;
    public string VirtualMachineName => Intent.Target.Name;
    public Guid VirtualMachineId => Intent.Target.VirtualMachineId;
    public Guid ProviderId => Intent.Target.ProviderId;
}

public sealed record AssistantMiniComputerOperationPreparation(AssistantMiniComputerOperationPreview? Preview, string Reason);
public sealed record AssistantMiniComputerOperationResult(string State, DateTimeOffset? ObservedAt,
    bool WasDispatched, bool IsPending, string Reason);
public sealed record AssistantMiniComputerPendingObservation(string? RequestId, string Reason, bool IsPending);
public sealed record AssistantMiniComputerSelectionResult(bool Saved, string Reason);

/// <summary>Per-presentation workflow borrowing the SAME canonical Assistant bridge
/// and global Mini Computer engine source. Closing this view never powers off a VM.</summary>
public interface IAssistantMiniComputerController : IAsyncDisposable
{
    bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge bridge);
    Task<AssistantMiniComputerView> ReadAsync(AssistantConversationBinding binding, CancellationToken token = default);
    Task<AssistantMiniComputerSelectionResult> SelectAsync(AssistantMiniComputerView sameView,
        AssistantMiniComputerChoice sameChoice, CancellationToken token = default);
    Task<AssistantMiniComputerOperationPreparation> PrepareAsync(AssistantMiniComputerView sameView,
        CanonicalMiniComputerAction action, Guid operationId, CancellationToken token = default);
    Task<AssistantMiniComputerOperationResult> ExecuteAsync(AssistantMiniComputerOperationPreview samePreview,
        CancellationToken token = default);
    AssistantMiniComputerPendingObservation ObserveOriginalOperation(AssistantMiniComputerOperationPreview samePreview);
    Task? OriginalClose { get; }
    void DemandExternalOriginalRetirementJoin();
    void RequestRetirement();
    Task CloseAndDrainAsync();
}
