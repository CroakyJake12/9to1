using System.Runtime.CompilerServices;
using MembershipMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.MembershipMetadata;
using CompatibleCheckpointMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.CompatibleCheckpointMetadata;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

// This optional journey publishes a durable pause only after the exact finite
// producer stage and the actual Den CAS both acknowledge. The existing binding-
// only API keeps its conservative pending/unknown failure behavior.
public sealed partial class DenAssistantCanonicalBridge : IAssistantOriginalCompatibleConversationCheckpointOwner
{
    private readonly ConditionalWeakTable<AssistantCompatibleConversationCreationOutcome, object> _compatibleOutcomes = new();

    public bool IsIssuedOriginalCheckpoint(AssistantCompatibleConversationCheckpoint sameCheckpoint) =>
        _development is DenAssistantOriginalDevelopmentOwner actual &&
        actual.IsIssuedOriginalCompatibleCheckpoint(sameCheckpoint);

    public bool IsIssuedOriginalCreationOutcome(AssistantCompatibleConversationCreationOutcome sameOutcome) =>
        sameOutcome is not null && _compatibleOutcomes.TryGetValue(sameOutcome, out _);

    public Task<IReadOnlyList<AssistantCompatibleConversationCheckpoint>> ReadOriginalDeclinedPendingAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, int maximum, CancellationToken token) =>
        _originals.Admit<IReadOnlyList<AssistantCompatibleConversationCheckpoint>>(async () =>
        {
            if (maximum is < 1 or > 1000)
                throw new AssistantCommandRefusedException("Pending conversation count must be between 1 and 1000.");
            var owner = RequireCompatibleCheckpointOwner();
            var home = await OpenHomeAsync(token).ConfigureAwait(false);
            await DemandCompatibleDefinitionAsync(home, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
            var sessions = await _originals.Source(() => home.Den.ListAsync<SessionRecord>(identity.NamespaceId, token)).ConfigureAwait(false);
            var checkpoints = new List<AssistantCompatibleConversationCheckpoint>();
            foreach (var session in sessions.OrderByDescending(row => row.UpdatedAtUtc))
            {
                var metadata = ReadMetadata<MembershipMetadata>(session, SessionKey);
                if (metadata is not { Schema: 1, Publication: "declined-pending" } ||
                    metadata.DefinitionId != identity.DefinitionId) continue;
                var checkpoint = await IssueCompatibleCheckpointAsync(owner, identity,
                    expectedDefinitionRevision, session, metadata, token).ConfigureAwait(false);
                checkpoints.Add(checkpoint);
                if (checkpoints.Count == maximum) break;
            }
            await DemandCompatibleDefinitionAsync(await OpenHomeAsync(token).ConfigureAwait(false),
                identity, expectedDefinitionRevision, token).ConfigureAwait(false);
            return checkpoints;
        });

    public Task<AssistantCompatibleConversationCreationOutcome> CreateOriginalCompatibleConversationAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, Guid plannedConversationId,
        string title, Guid originalCreationOperationId, Guid commandOperationId,
        AssistantOriginalProjectChoice actualProjectChoice, CancellationToken token) => _originals.Admit(async () =>
        {
            DemandCompatibleOperationIds(plannedConversationId, originalCreationOperationId, commandOperationId);
            var owner = RequireCompatibleCheckpointOwner();
            var home = await OpenHomeAsync(token).ConfigureAwait(false);
            await DemandCompatibleDefinitionAsync(home, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
            var prior = await _originals.Source(() => home.Den.GetAsync<SessionRecord>(identity.NamespaceId,
                SessionId(plannedConversationId), token)).ConfigureAwait(false);
            // Only the actual private project-choice issuer selects the maintained
            // owner path. An existing Tasks container needs no Studio conversion or
            // newly allocated pair; fresh Home/store/READ validation remains in that
            // original creator, followed by its actual create-only membership flow.
            if (_originals.Invoke(() => owner.ObserveOriginalProjectChoiceMode(actualProjectChoice)) == HavenMode.Tasks)
            {
                if (prior is not null && ReadMetadata<MembershipMetadata>(prior, SessionKey) is { Schema: 1 } taskMembership &&
                    taskMembership.OriginalConversation.Title != title)
                    throw new AssistantCommandRefusedException("This creation operation already reserves another conversation title.");
                var actualBinding = await _originals.Source(() => CreateProjectConversationOriginalAsync(identity,
                    expectedDefinitionRevision, plannedConversationId, title, originalCreationOperationId,
                    actualProjectChoice, token)).ConfigureAwait(false);
                return PublishCompatibleOutcome(actualBinding, null);
            }
            if (prior is not null)
            {
                var metadata = RequireCompatibleMetadata(prior, identity, plannedConversationId, originalCreationOperationId);
                if (metadata.OriginalConversation.Title != title)
                    throw new AssistantCommandRefusedException("This creation operation already reserves another conversation title.");
                await DemandCompatibleChoiceAsync(owner, actualProjectChoice, metadata, home.Actor, token).ConfigureAwait(false);
                if (metadata.Publication == "ready")
                    return PublishCompatibleOutcome(await BindAsync(home, identity, prior, token).ConfigureAwait(false), null);
                if (metadata.Publication == "declined-pending")
                    return PublishCompatibleOutcome(null, await IssueCompatibleCheckpointAsync(owner, identity,
                        expectedDefinitionRevision, prior, metadata, token).ConfigureAwait(false));
                throw new AssistantCompatibleTaskPublicationPendingException(plannedConversationId,
                    originalCreationOperationId, prior.Revision);
            }

            SessionRecord? pending = null;
            MembershipMetadata? pendingMetadata = null;
            try
            {
                var stage = await _originals.Source(() => owner.CreateOriginalCompatibleTaskContextCheckpointWithinSourceAsync(
                    actualProjectChoice, plannedConversationId, title, originalCreationOperationId,
                    ReservePendingAsync, MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
                return await PublishCompatibleStageAsync(owner, identity, expectedDefinitionRevision,
                    commandOperationId, pending, pendingMetadata, stage, token).ConfigureAwait(false);
            }
            catch (Exception cause) when (pending is not null)
            {
                throw new AssistantCompatibleTaskPublicationPendingException(plannedConversationId,
                    originalCreationOperationId, pending.Revision, cause);
            }

            async Task ReservePendingAsync(ICanonicalProjectTaskContextCreationIntent intent)
            {
                var current = await OpenHomeAsync(token).ConfigureAwait(false);
                await DemandCompatibleDefinitionAsync(current, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
                if (pending is not null || current.Actor != intent.Actor || intent.TaskConversation.Id != plannedConversationId ||
                    intent.OperationId != originalCreationOperationId)
                    throw new AssistantCommandRefusedException("The exact compatible creation actor or allocated original changed before Den reservation.");
                var digest = _originals.Invoke(() => owner.GetOriginalCompatibleTaskIntentDigest(intent));
                pendingMetadata = new MembershipMetadata(1, identity.DefinitionId, originalCreationOperationId,
                    AssistantConversationKind.Task, "pending", intent.TaskConversation, actualProjectChoice.Project.Reference,
                    intent.OriginalStudioConversation.Id, intent.OriginalStudioContainer.Id, intent.TaskContainer,
                    intent.OriginalStudioConversation, intent.OriginalStudioContainer, digest,
                    intent.OriginalStoreObservation.OriginalStoreIdentity);
                var row = new SessionRecord { Id = SessionId(plannedConversationId), NamespaceId = identity.NamespaceId,
                    ConversationId = plannedConversationId.ToString("D"), ExtensionData = WriteMetadata(null, SessionKey, pendingMetadata) };
                row = row with { ExtensionData = WriteMetadata(row.ExtensionData,
                    AssistantCanonicalMembershipSource.CompatibleCheckpointKey,
                    new CompatibleCheckpointMetadata(1, expectedDefinitionRevision, commandOperationId, "")) };
                pending = await _originals.Source(() => current.Den.SaveAsync(row, 0,
                    Operation(commandOperationId, "compatible.reserve"), token)).ConfigureAwait(false);
                await DemandCompatibleDefinitionAsync(current, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
            }
        });

    public Task<AssistantCompatibleConversationCreationOutcome> ResumeOriginalCompatibleConversationAsync(
        AssistantCompatibleConversationCheckpoint sameCheckpoint, long expectedDefinitionRevision,
        Guid newCommandOperationId, AssistantOriginalProjectChoice freshActualProjectChoice,
        CancellationToken token) => _originals.Admit(async () =>
        {
            var owner = RequireCompatibleCheckpointOwner();
            if (sameCheckpoint is null || !owner.IsIssuedOriginalCompatibleCheckpoint(sameCheckpoint) ||
                !ReferenceEquals(sameCheckpoint.Issuer, owner) || sameCheckpoint.DefinitionRevision != expectedDefinitionRevision ||
                newCommandOperationId == Guid.Empty || newCommandOperationId == sameCheckpoint.LastCommandOperationId)
                throw new AssistantCommandRefusedException("Resume requires the SAME issued durable checkpoint and a fresh command identity.");
            var identity = sameCheckpoint.Identity;
            var home = await OpenHomeAsync(token).ConfigureAwait(false);
            await DemandCompatibleDefinitionAsync(home, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
            var prior = await _originals.Source(() => home.Den.GetAsync<SessionRecord>(identity.NamespaceId,
                sameCheckpoint.DenSessionId, token)).ConfigureAwait(false)
                ?? throw new AssistantCommandRefusedException("The actual pending Den checkpoint is unavailable.");
            var metadata = RequireCompatibleMetadata(prior, identity, sameCheckpoint.PlannedConversationId,
                sameCheckpoint.OriginalCreationOperationId);
            var details = ReadMetadata<CompatibleCheckpointMetadata>(prior, AssistantCanonicalMembershipSource.CompatibleCheckpointKey);
            if (metadata.Publication != "declined-pending" || prior.Revision != sameCheckpoint.MembershipRevision ||
                details is not { Schema: 1 } || details.DefinitionRevision != expectedDefinitionRevision ||
                details.LastCommandOperation != sameCheckpoint.LastCommandOperationId ||
                metadata.OriginalConversation != sameCheckpoint.OriginalTaskConversation ||
                metadata.OriginalTaskContainer != sameCheckpoint.OriginalTaskContainer ||
                metadata.OriginalStoreIdentity != sameCheckpoint.OriginalStoreIdentity)
                throw new AssistantCommandRefusedException("The actual pending checkpoint changed; read a fresh source-issued checkpoint before resuming.");
            await DemandCompatibleChoiceAsync(owner, freshActualProjectChoice, metadata, home.Actor, token).ConfigureAwait(false);
            SessionRecord? pendingAttempt = null;
            try
            {
                var stage = await _originals.Source(() => owner.ResumeOriginalCompatibleTaskContextCheckpointWithinSourceAsync(
                    sameCheckpoint, freshActualProjectChoice, newCommandOperationId, ReserveAttemptAsync,
                    MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
                return await PublishCompatibleStageAsync(owner, identity, expectedDefinitionRevision, newCommandOperationId,
                    pendingAttempt, metadata with { Publication = "pending" }, stage, token).ConfigureAwait(false);
            }
            catch (Exception cause) when (pendingAttempt is not null)
            {
                throw new AssistantCompatibleTaskPublicationPendingException(sameCheckpoint.PlannedConversationId,
                    sameCheckpoint.OriginalCreationOperationId, pendingAttempt.Revision, cause);
            }

            async Task ReserveAttemptAsync(ICanonicalProjectTaskContextCreationIntent intent)
            {
                var current = await OpenHomeAsync(token).ConfigureAwait(false);
                await DemandCompatibleDefinitionAsync(current, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
                if (pendingAttempt is not null || current.Actor != intent.Actor || intent.OperationId != sameCheckpoint.OriginalCreationOperationId ||
                    intent.TaskConversation != metadata.OriginalConversation || intent.TaskContainer != metadata.OriginalTaskContainer ||
                    intent.OriginalStudioConversation != metadata.OriginalStudioConversation ||
                    intent.OriginalStudioContainer != metadata.OriginalStudioContainer ||
                    intent.OriginalStoreObservation.OriginalStoreIdentity != metadata.OriginalStoreIdentity ||
                    _originals.Invoke(() => owner.GetOriginalCompatibleTaskIntentDigest(intent)) != metadata.OriginalCreationIntentSha256)
                    throw new AssistantCommandRefusedException("Resume did not prepare the SAME complete allocated creation tuple.");
                var row = prior with { ExtensionData = WriteMetadata(prior.ExtensionData, SessionKey,
                    metadata with { Publication = "pending" }) };
                row = row with { ExtensionData = WriteMetadata(row.ExtensionData,
                    AssistantCanonicalMembershipSource.CompatibleCheckpointKey,
                    details with { LastCommandOperation = newCommandOperationId, Reason = "" }) };
                pendingAttempt = await _originals.Source(() => current.Den.SaveAsync(row, prior.Revision,
                    Operation(newCommandOperationId, "compatible.resume"), token)).ConfigureAwait(false);
                await DemandCompatibleDefinitionAsync(current, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
            }
        });

    private async Task<AssistantCompatibleConversationCreationOutcome> PublishCompatibleStageAsync(
        DenAssistantOriginalDevelopmentOwner owner, AssistantIdentity identity, long expectedDefinitionRevision,
        Guid commandOperationId, SessionRecord? pending, MembershipMetadata? metadata,
        DenAssistantOriginalDevelopmentOwner.CompatibleTaskContextCreationStage stage, CancellationToken token)
    {
        if (pending is null || metadata is null || !owner.IsIssuedOriginalCompatibleTaskContextStage(stage) ||
            stage.OriginalIntent.TaskConversation != metadata.OriginalConversation ||
            stage.OriginalIntent.TaskContainer != metadata.OriginalTaskContainer ||
            stage.OriginalIntent.OperationId != metadata.CreationOperation || stage.OriginalIntentSha256 != metadata.OriginalCreationIntentSha256 ||
            stage.StudioConversation != metadata.OriginalStudioConversation || stage.StudioContainer != metadata.OriginalStudioContainer ||
            stage.OriginalIntent.OriginalStoreObservation.OriginalStoreIdentity != metadata.OriginalStoreIdentity ||
            stage.Project.Reference != metadata.OriginalProjectReference)
            throw new InvalidOperationException("The actual finite stage differs from the durably reserved full creation tuple.");
        var home = await OpenHomeAsync(token).ConfigureAwait(false);
        await DemandCompatibleDefinitionAsync(home, identity, expectedDefinitionRevision, token).ConfigureAwait(false);
        if (home.Actor != stage.OriginalIntent.Actor || (stage.Creation is null && !stage.IsDeclinedNoSql))
            throw new InvalidOperationException("The actual stage did not settle one acknowledged creation or exact no-SQL decline.");
        var declined = stage.IsDeclinedNoSql;
        const string reason = "The individual WRITE acknowledged no canonical SQL effect. The allocated pair remains durably pending; fresh READ and WRITE are required to resume.";
        var row = pending with { ExtensionData = WriteMetadata(pending.ExtensionData, SessionKey,
            metadata with { Publication = declined ? "declined-pending" : "ready" }) };
        row = row with { ExtensionData = WriteMetadata(row.ExtensionData,
            AssistantCanonicalMembershipSource.CompatibleCheckpointKey,
            new CompatibleCheckpointMetadata(1, expectedDefinitionRevision, commandOperationId, declined ? reason : "")) };
        var acknowledged = await _originals.Source(() => home.Den.SaveAsync(row, pending.Revision,
            Operation(commandOperationId, declined ? "compatible.declined-checkpoint" : "compatible.ready"), token)).ConfigureAwait(false);
        if (!declined)
            return PublishCompatibleOutcome(await BindAsync(home, identity, acknowledged, token).ConfigureAwait(false), null);
        var checkpoint = await IssueCompatibleCheckpointAsync(owner, identity, expectedDefinitionRevision,
            acknowledged, metadata with { Publication = "declined-pending" }, token).ConfigureAwait(false);
        return PublishCompatibleOutcome(null, checkpoint);
    }

    private async Task<AssistantCompatibleConversationCheckpoint> IssueCompatibleCheckpointAsync(
        DenAssistantOriginalDevelopmentOwner owner, AssistantIdentity identity, long definitionRevision,
        SessionRecord acknowledged, MembershipMetadata metadata, CancellationToken token)
    {
        metadata = RequireCompatibleMetadata(acknowledged, identity, metadata.OriginalConversation.Id, metadata.CreationOperation);
        var details = ReadMetadata<CompatibleCheckpointMetadata>(acknowledged, AssistantCanonicalMembershipSource.CompatibleCheckpointKey);
        if (metadata.Publication != "declined-pending" || details is not { Schema: 1 } ||
            details.DefinitionRevision != definitionRevision || details.LastCommandOperation == Guid.Empty || string.IsNullOrWhiteSpace(details.Reason))
            throw new AssistantCommandRefusedException("No acknowledged current-definition declined checkpoint exists for this original.");
        var checkpoint = new AssistantCompatibleConversationCheckpoint(owner, identity, definitionRevision,
            acknowledged.Id, acknowledged.Revision, metadata.OriginalConversation.Id, metadata.CreationOperation,
            details.LastCommandOperation, metadata.OriginalConversation.Title, metadata.OriginalProjectReference!,
            metadata.OriginalStudioConversation!.Id, metadata.OriginalStudioContainer!.Id, metadata.OriginalStoreIdentity!,
            metadata.OriginalConversation, metadata.OriginalTaskContainer!, details.Reason);
        await _originals.Source(() => owner.RegisterOriginalCompatibleCheckpointWithinSourceAsync(checkpoint,
            _membership, acknowledged, MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
        if (!owner.IsIssuedOriginalCompatibleCheckpoint(checkpoint))
            throw new InvalidOperationException("The configured process owner did not issue the SAME acknowledged checkpoint.");
        return checkpoint;
    }

    private DenAssistantOriginalDevelopmentOwner RequireCompatibleCheckpointOwner() =>
        _development as DenAssistantOriginalDevelopmentOwner ??
        throw new AssistantCommandRefusedException("The actual compatible canonical context owner is unavailable.");

    private async Task DemandCompatibleDefinitionAsync(HomePersonalDenSession home, AssistantIdentity identity,
        long expectedDefinitionRevision, CancellationToken token)
    {
        var definition = await DefinitionAsync(home, identity, token).ConfigureAwait(false);
        if (definition.Revision != expectedDefinitionRevision)
            throw new DenException(DenErrorCode.Conflict, "The configured definition changed before this compatible checkpoint operation.", recoverable: true);
    }

    private static void DemandCompatibleOperationIds(Guid conversationId, Guid originalCreationOperationId, Guid commandOperationId)
    {
        if (conversationId == Guid.Empty || originalCreationOperationId == Guid.Empty || commandOperationId == Guid.Empty)
            throw new AssistantCommandRefusedException("Canonical conversation, stable creation and explicit command identities are required.");
    }

    private static MembershipMetadata RequireCompatibleMetadata(SessionRecord session, AssistantIdentity identity,
        Guid conversationId, Guid originalCreationOperationId)
    {
        var metadata = ReadMetadata<MembershipMetadata>(session, SessionKey);
        if (metadata is not { Schema: 1, Kind: AssistantConversationKind.Task } ||
            session.NamespaceId != identity.NamespaceId || session.Id != SessionId(conversationId) ||
            session.ConversationId != conversationId.ToString("D") || metadata.DefinitionId != identity.DefinitionId ||
            metadata.CreationOperation != originalCreationOperationId || metadata.OriginalConversation.Id != conversationId ||
            metadata.OriginalTaskContainer is null || metadata.OriginalStudioConversation is null || metadata.OriginalStudioContainer is null ||
            metadata.OriginalProjectReference is null || metadata.OriginalStoreIdentity is null ||
            string.IsNullOrWhiteSpace(metadata.OriginalCreationIntentSha256) ||
            metadata.OriginalConversation.ContainerId != metadata.OriginalTaskContainer.Id ||
            metadata.OriginalStudioConversation.Id != metadata.OriginalStudioConversationId ||
            metadata.OriginalStudioContainer.Id != metadata.OriginalStudioContainerId)
            throw new AssistantCommandRefusedException("The actual Den record does not preserve this full compatible creation lineage.");
        return metadata;
    }

    private async Task DemandCompatibleChoiceAsync(DenAssistantOriginalDevelopmentOwner owner,
        AssistantOriginalProjectChoice choice, MembershipMetadata metadata, AuthenticatedResourceActor actor, CancellationToken token)
    {
        var current = await _originals.Source(() => owner.ValidateOriginalProjectChoiceWithinSourceAsync(choice,
            MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
        if (current.Actor != actor || current.Context != metadata.OriginalStudioConversation ||
            current.Container != metadata.OriginalStudioContainer || current.Project.Reference != metadata.OriginalProjectReference)
            throw new AssistantCommandRefusedException("Choose a fresh authorized READ of the SAME original Studio project.");
    }

    private AssistantCompatibleConversationCreationOutcome PublishCompatibleOutcome(
        AssistantConversationBinding? binding, AssistantCompatibleConversationCheckpoint? checkpoint)
    {
        var outcome = new AssistantCompatibleConversationCreationOutcome(this, binding, checkpoint);
        _compatibleOutcomes.Add(outcome, this);
        return outcome;
    }
}
