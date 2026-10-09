using Haven.Application;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Per-presentation workflow borrowing the SAME global Chat memory source and
/// canonical bridge. Retire/join this controller before the borrowed bridge; app shutdown
/// later joins the memory source, Home WRITE owner and actual stores.</summary>
public sealed partial class AssistantMemoryManagementController : IAssistantMemoryManagementController, IAssistantMemoryRevisionManagementController
{
    private readonly AssistantOriginalMemorySource _source;
    private readonly IAssistantCanonicalBridge _bridge;
    private readonly object _issuer = new();
    private readonly AssistantMemoryOriginals _originals = new();
    public AssistantMemoryManagementController(AssistantOriginalMemorySource source, IAssistantCanonicalBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(bridge);
        if (bridge is not DenAssistantCanonicalBridge actual || !source.HasOriginalHomeDenFactory(actual.OriginalHomeDenFactory))
            throw new ArgumentException("Memory management must borrow the SAME original Home Den and canonical bridge.");
        _source = source; _bridge = bridge;
    }
    public bool IsOriginalCanonicalBridge(IAssistantCanonicalBridge bridge) => ReferenceEquals(_bridge, bridge);
    public Task<AssistantMemoryView> ReadAsync(AssistantConversationBinding binding, CancellationToken token = default) =>
        ReadPageAsync(binding, 32, null, null, token);
    public Task<AssistantMemoryWritePreview> PrepareAsync(AssistantMemoryView view, string title, string summary,
        Guid operationId, CancellationToken token = default) => Run(async scope =>
    {
        if (!ReferenceEquals(view.Issuer, _issuer) || view.Input is null)
            throw new UnauthorizedAccessException("Refresh this presentation's actual permitted Assistant memory before preparing a write.");
        var intent = await scope.Read(() => _source.PrepareOriginalWriteWithinSourceAsync(view.Input,
            view.Binding.Conversation, title, summary, operationId, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        return new AssistantMemoryWritePreview(_issuer, intent);
    });
    public Task<AssistantMemorySaveResult> CommitAsync(AssistantMemoryWritePreview preview, CancellationToken token = default) => Run(async scope =>
    {
        if (!ReferenceEquals(preview.Issuer, _issuer) || !_source.IsIssuedOriginalWriteIntent(preview.Intent))
            throw new UnauthorizedAccessException("Use this presentation's SAME original memory preview.");
        Task<ICanonicalAssistantMemoryWriteAcknowledgment>? actual = null;
        try
        {
            var acknowledgment = await scope.Read(() => actual = _source.CommitOriginalWriteWithinSourceAsync(
                preview.Intent, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            if (!ReferenceEquals(acknowledgment.OriginalIntent, preview.Intent) || actual is null ||
                !_source.IsOwnedOriginalPublicWriteAcknowledgment(preview.Intent, acknowledgment, actual))
                throw new InvalidOperationException("The actual memory acknowledgment belongs to a different operation.");
            if (acknowledgment is ICanonicalAssistantMemoryWriteDecisionAcknowledgment { Applied: false } declined)
                return new AssistantMemorySaveResult(false, null, declined.Reason);
            return new AssistantMemorySaveResult(true, acknowledgment.Record,
                acknowledgment is ICanonicalAssistantMemoryWriteDecisionAcknowledgment decision ? decision.Reason : "The private Assistant preference was saved in the existing Knowledge store.");
        }
        catch (Exception)
        {
            if (actual is null || !scope.AcknowledgeOriginalRefusal(actual, _source.IsAcknowledgedOriginalWriteRefusal)) throw;
            scope.AcknowledgeOriginalRefusalOccurrences(_source.IsAcknowledgedOriginalWriteRefusal);
            await scope.JoinAsync().ConfigureAwait(false);
            return new AssistantMemorySaveResult(false, null, "Home declined this write before persistence. Your original draft and operation remain available.");
        }
    });
    public Task<AssistantMemoryRevisionPreparation> PrepareRevisionAsync(AssistantMemoryView view,
        Haven.Core.KnowledgeRecord selected, CanonicalAssistantMemoryMutationKind kind, string title,
        string summary, Guid operationId, CancellationToken token = default) => Run(async scope =>
    {
        if (!ReferenceEquals(view.Issuer, _issuer) || view.Input is null ||
            !view.Records.Any(record => ReferenceEquals(record, selected)))
            return new AssistantMemoryRevisionPreparation(null, "Select a record from this presentation's current permitted memory view.");
        var prepared = await scope.Read(() => _source.PrepareOriginalRevisionWithinSourceAsync(view.Input,
            view.Binding.Conversation, selected, kind, title, summary, operationId, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        return new AssistantMemoryRevisionPreparation(prepared.Intent is null ? null : new AssistantMemoryWritePreview(_issuer, prepared.Intent), prepared.Reason);
    });
    private Task<T> Run<T>(Func<AssistantMemoryOriginals.Scope, Task<T>> operation) => _originals.Admit(async () =>
    {
        var scope = _originals.CreateScope(body => body(), _ => { }); T result = default!; var failures = new List<Exception>();
        try { result = await operation(scope).ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        try { await scope.JoinAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        AssistantMemoryOriginals.Throw(failures); return result;
    });
    public Task? OriginalClose => _originals.OriginalClose;
    public void DemandExternalOriginalRetirementJoin() => _originals.DemandExternalOriginalRetirementJoin();
    public void RequestRetirement() => _originals.RequestRetirement();
    public Task CloseAndDrainAsync() => _originals.CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
