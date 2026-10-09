using Haven.Application;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed partial class AssistantMiniComputerController : IAssistantMiniComputerController
{
    private readonly AssistantMiniComputerSource _source;
    private readonly AssistantsWorkspaceController _workspace;
    private readonly object _issuer = new();
    private readonly AssistantMiniComputerOriginals _originals = new();
    private AssistantMiniComputerView? _currentView;
    private long _readGeneration;

    public AssistantMiniComputerController(AssistantMiniComputerSource source, AssistantsWorkspaceController actualWorkspace)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(actualWorkspace);
        if (actualWorkspace.OriginalCanonicalBridge is not DenAssistantCanonicalBridge bridge ||
            !source.HasOriginalHomeDenFactory(bridge.OriginalHomeDenFactory))
            throw new ArgumentException("Mini Computer must borrow the SAME actual Assistant workspace and Home Den.");
        _source = source; _workspace = actualWorkspace;
    }
    public bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge bridge) => ReferenceEquals(_workspace.OriginalCanonicalBridge, bridge);
    public Task<AssistantMiniComputerView> ReadAsync(AssistantConversationBinding binding, CancellationToken token = default) =>
        _originals.Run(body => body(), _ => { }, async scope =>
        {
            var generation = Interlocked.Increment(ref _readGeneration);
            var result = await scope.Read(() => _source.ReadWithinSourceAsync(binding, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            AssistantMiniComputerChoice[] choices = result.Input is null ? [] : result.Input.Catalog.Snapshot.VirtualMachines.Select(vm =>
                new AssistantMiniComputerChoice(_issuer, vm.VMID.Value, vm.ProviderID.Value, vm.Name,
                    vm.GuestFamily, vm.Revision, vm.LifecycleState.ToString())).ToArray();
            var configured = Guid.TryParse(binding.Definition.Configuration.MiniComputerId, out var id) ? id : Guid.Empty;
            var view = new AssistantMiniComputerView(_issuer, binding, Array.AsReadOnly(choices),
                choices.SingleOrDefault(choice => choice.VirtualMachineId == configured), result.Reason,
                result.Input is not null, result.Input);
            if (generation == Volatile.Read(ref _readGeneration)) _currentView = view;
            return view;
        });
    public Task<AssistantMiniComputerSelectionResult> SelectAsync(AssistantMiniComputerView view,
        AssistantMiniComputerChoice choice, CancellationToken token = default) => _originals.Run(body => body(), _ => { }, async scope =>
    {
        if (!ReferenceEquals(view, _currentView) || !ReferenceEquals(view.Issuer, _issuer) || !view.IsOriginalChoice(choice) ||
            view.Original is not AssistantMiniComputerSource.CatalogInput input ||
            !ReferenceEquals(_workspace.Snapshot.ConversationBinding, view.Binding))
            return new AssistantMiniComputerSelectionResult(false, "Select a VM from this presentation's current permitted catalogue.");
        var current = await scope.Read(() => _source.ReadWithinSourceAsync(view.Binding, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        if (current.Input is null || current.Input.Catalog.Sha256 != input.Catalog.Sha256 ||
            !ReferenceEquals(_workspace.Snapshot.ConversationBinding, view.Binding))
            return new AssistantMiniComputerSelectionResult(false, "The original VM catalogue or Assistant changed. Refresh before choosing a VM.");
        var definition = view.Definition;
        Task? save = null;
        try
        {
            await scope.Read(() => save = _workspace.ConfigureAsync(definition.Identity, definition.Revision,
                definition.Configuration with { MiniComputerId = choice.VirtualMachineId.ToString("D") }, Guid.NewGuid(), token)).ConfigureAwait(false);
        }
        catch
        {
            if (save is null || !_workspace.IsAcknowledgedOriginalCommandRefusal(save) ||
                !scope.AcknowledgeOriginalRefusal(save, _workspace.IsAcknowledgedOriginalCommandRefusal)) throw;
            return new AssistantMiniComputerSelectionResult(false, "The actual Assistant changed before this preference could be saved. Refresh the current view.");
        }
        return new AssistantMiniComputerSelectionResult(true,
            "The VM preference is saved for this Assistant. Each provider inspection or lifecycle action still requires its own Home approval.");
    });
    public Task<AssistantMiniComputerOperationPreparation> PrepareAsync(AssistantMiniComputerView view,
        CanonicalMiniComputerAction action, Guid operationId, CancellationToken token = default) => _originals.Run(body => body(), _ => { }, async scope =>
    {
        if (!ReferenceEquals(view, _currentView) || !ReferenceEquals(view.Issuer, _issuer) || view.Original is not AssistantMiniComputerSource.CatalogInput input ||
            !ReferenceEquals(_workspace.Snapshot.ConversationBinding, view.Binding))
            return new AssistantMiniComputerOperationPreparation(null, "Refresh this original Assistant's VM view before preparing an action.");
        var intent = await scope.Read(() => _source.PrepareWithinSourceAsync(input, action, operationId,
            scope.Run, scope.Retain, token)).ConfigureAwait(false);
        return new AssistantMiniComputerOperationPreparation(intent is null ? null : new(_issuer, intent),
            intent is null ? "Choose and save a current VM first. The enabled Assistant and exact Home operation source must remain available."
                : "This exact VM and action will be reviewed separately in Home. The host desktop is outside this operation.");
    });
    public Task<AssistantMiniComputerOperationResult> ExecuteAsync(AssistantMiniComputerOperationPreview preview,
        CancellationToken token = default) => _originals.Run(body => body(), _ => { }, async scope =>
    {
        if (!ReferenceEquals(preview.Issuer, _issuer) || !_source.IsIssuedOriginalOperationIntent(preview.Intent))
            return new AssistantMiniComputerOperationResult("NotObserved", null, false, false, "Review this presentation's SAME exact VM action.");
        var result = await scope.Read(() => _source.ExecuteWithinSourceAsync(preview.Intent,
            scope.Run, scope.Retain, token)).ConfigureAwait(false);
        return result.Acknowledgment is { } acknowledgment
            ? new(acknowledgment.ObservedState, acknowledgment.ObservedAt, acknowledgment.WasDispatched, acknowledgment.IsPending, acknowledgment.Reason)
            : new("NotObserved", null, false, false, result.Reason);
    });
    public AssistantMiniComputerPendingObservation ObserveOriginalOperation(AssistantMiniComputerOperationPreview preview) =>
        ReferenceEquals(preview.Issuer, _issuer) && _source.IsIssuedOriginalOperationIntent(preview.Intent)
            ? _source.Observe(preview.Intent) : new(null, "This action preview belongs to another presentation.", false);
    public Task? OriginalClose => _originals.OriginalClose;
    public void DemandExternalOriginalRetirementJoin() => _originals.DemandExternalJoin();
    public void RequestRetirement() => _originals.RequestRetirement();
    public Task CloseAndDrainAsync() => _originals.CloseAndDrain();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
