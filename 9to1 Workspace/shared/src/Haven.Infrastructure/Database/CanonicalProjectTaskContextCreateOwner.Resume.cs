using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

// PRIVATE additive source-owned resume producer. Den-issued checkpoint metadata never
// grants store/content/effect authority and never adopts caller-supplied existing IDs.
public sealed partial class CanonicalProjectTaskContextCreateOwner
{
    public VerifiedResourceStoreOwnership? GetOriginalCreationDenOwnership(ICanonicalProjectTaskContextCreationIntent sameIntent) =>
        RequireIntent(sameIntent).OriginalDenOwnership;
    private ICanonicalProjectTaskContextResumeSelectionSource RequireResumeSource(
        ICanonicalProjectTaskContextResumeSelection selection)
    {
        var actual = OriginalResumeSelectionSource ?? throw new InvalidOperationException("The actual compatible-conversation checkpoint source is unavailable.");
        if (!actual.IsIssuedOriginalResumeSelection(selection))
            throw new UnauthorizedAccessException("The SAME configured Den owner did not issue this actual resume selection.");
        return actual;
    }
    private async Task DemandResumeSelectionAsync(Intent intent, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        if (intent.ResumeSelection is not { } selection) return;
        ICanonicalProjectTaskContextResumeSelectionSource owner = null!;
        source.Run(() => { owner = RequireResumeSource(selection); });
        await source.Read(() => owner.RevalidateOriginalResumeSelectionWithinSourceAsync(selection,
            intent.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
        source.Run(() =>
        {
            DemandResumeTuple(selection, intent.OriginalStoreObservation, intent.OriginalProjectRead, intent.Durable);
            if (!ReferenceEquals(selection.OriginalDenOwnership, intent.OriginalDenOwnership))
                throw new UnauthorizedAccessException("The original Den ownership observation changed before resume.");
        });
    }
    private static void DemandResumeTuple(ICanonicalProjectTaskContextResumeSelection selection,
        ICanonicalProjectContextStoreObservation observation, IDeveloperOriginalProjectCommandRead read, Receipt receipt)
    {
        var descriptor = read.OriginalDescriptor; var denOwnership = selection.OriginalDenOwnership;
        if (denOwnership is null || denOwnership.Receipt is null || denOwnership.ResourceKind != "den" ||
            denOwnership.StoreId != selection.DenId || denOwnership.ProfileId != selection.Actor.ProfileId ||
            selection.Actor != observation.Actor || selection.OriginalStoreIdentity != observation.OriginalStoreIdentity ||
            selection.OriginalStudioConversation != receipt.Studio || selection.OriginalStudioContainer != receipt.StudioContainer ||
            selection.TaskConversation != receipt.Task || selection.TaskContainer != receipt.TaskContainer ||
            selection.OriginalCreationOperationId != receipt.OperationId || receipt.OperationId == Guid.Empty ||
            receipt.SchemaVersion != 1 || receipt.StoreId != observation.OriginalStoreIdentity.StoreId ||
            receipt.ActorId != observation.Actor.ActorId || receipt.ProfileId != observation.Actor.ProfileId ||
            receipt.Task.Id == Guid.Empty || receipt.Task.Id == receipt.Studio.Id || receipt.TaskContainer.Id == Guid.Empty ||
            receipt.TaskContainer.Id == receipt.StudioContainer.Id || receipt.Task.ContainerId != receipt.TaskContainer.Id ||
            receipt.Task.Mode != HavenMode.Tasks || receipt.Task.Kind != ConversationKind.Task ||
            receipt.Task.IsArchived || receipt.Task.IsTemporary || receipt.Task.IsPinned ||
            receipt.Task.SpaceId is not null || receipt.Task.LessonId is not null ||
            receipt.Task.ParentConversationId is not null || receipt.Task.CompactedAt is not null ||
            string.IsNullOrWhiteSpace(receipt.Task.Title) || receipt.Task.Title.Length > 512 ||
            receipt.TaskContainer.Mode != HavenMode.Tasks || receipt.TaskContainer.IsArchived ||
            receipt.TaskContainer.RootPath != descriptor.RegisteredProjectRoot ||
            receipt.TaskContainer.Context != descriptor.ExactProjectReferenceJson ||
            receipt.TaskContainer.Name != receipt.StudioContainer.Name ||
            receipt.TaskContainer.Instructions != receipt.StudioContainer.Instructions ||
            receipt.Task.CreatedAt != receipt.TaskContainer.CreatedAt ||
            receipt.Task.UpdatedAt != receipt.TaskContainer.UpdatedAt)
            throw new UnauthorizedAccessException("The actual Den checkpoint, original Studio and fresh authorized project tuple disagree.");
        DemandStudioTuple(observation, read, receipt.Studio, receipt.StudioContainer);
    }
    public Task<ICanonicalProjectTaskContextCreationIntent> PrepareOriginalResumedCreationIntentWithinSourceAsync(
        ICanonicalProjectTaskContextResumeSelection selection,
        ICanonicalProjectContextStoreObservation observation, IDeveloperOriginalProjectCommandRead read,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, WrapOriginalScope(scope), retain);
        return AdmitOriginal(() => _store.RetainOriginalReader<ICanonicalProjectTaskContextCreationIntent>(source, async () =>
        {
            ICanonicalProjectTaskContextResumeSelectionSource owner = null!;
        source.Run(() => { owner = RequireResumeSource(selection); });
            await source.Read(() => owner.RevalidateOriginalResumeSelectionWithinSourceAsync(selection,
                observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            await DemandSourceAsync(observation, read, source, token).ConfigureAwait(false);
            // Full source-issued pair is captured unchanged. No GUID allocation, timestamps,
            // root override, title replacement, SQL upsert or metadata-row adoption occurs.
            var receipt = source.Invoke(() => new Receipt(1, selection.OriginalStoreIdentity.StoreId,
                selection.Actor.ActorId, selection.Actor.ProfileId, selection.OriginalCreationOperationId,
                selection.OriginalStudioConversation, selection.OriginalStudioContainer,
                selection.TaskConversation, selection.TaskContainer));
            source.Run(() => DemandResumeTuple(selection, observation, read, receipt));
            var stored = await ReadReceiptAsync(observation.Actor, observation.OriginalStoreIdentity,
                receipt.OperationId, source, token).ConfigureAwait(false);
            if (stored is not null && stored != receipt)
                throw new InvalidDataException("The actual creation operation receipt conflicts with the durable Den checkpoint. Preserve both for explicit recovery.");
            await source.Read(() => owner.RevalidateOriginalResumeSelectionWithinSourceAsync(selection,
                observation.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
            await DemandSourceAsync(observation, read, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var denOwnership = source.Invoke(() => selection.OriginalDenOwnership);
            var intent = new Intent(this, observation, read, receipt, selection, denOwnership);
            source.Run(() =>
            {
                lock (_gate)
                {
                    foreach (var dead in _operations.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) _operations.Remove(dead);
                    if (_operations.Count >= 128 && !_operations.ContainsKey(receipt.OperationId))
                        throw new InvalidOperationException("Settle prepared creation metadata before another operation.");
                    if (_operations.TryGetValue(receipt.OperationId, out var prior) && prior.TryGetTarget(out var same) && same.Durable != receipt)
                        throw new InvalidOperationException("The SAME creation operation already names a different immutable pair; nothing was adopted or overwritten.");
                    _intents.Add(intent, intent); _operations[receipt.OperationId] = new(intent);
                }
            });
            return intent;
        }));
    }
}
