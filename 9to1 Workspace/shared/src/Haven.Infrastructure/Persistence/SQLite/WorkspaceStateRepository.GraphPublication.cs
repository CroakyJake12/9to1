using System.Text.Json;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceStateRepository
{
    /// <summary>Durable disabled-only preparation before Home graph publication. This is not a graph/run grant.
    /// Only an exact separately reviewed Update and the real owner-issued admission can write the journal.</summary>
    public Task<AutomationDefinitionCommitResult> PrepareGraphPublicationAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, CancellationToken ct) =>
        WriteGraphPublicationAsync(change, admission, null, null, null, ct);

    /// <summary>Same original trusted local Home composition is mandatory even for durable Prepared mutation.</summary>
    public Task<AutomationDefinitionCommitResult> PrepareGraphPublicationAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, HomeAutomationDefinitionCommitFenceSource originalHome,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalHome);
        return WriteGraphPublicationAsync(change, admission, null, null, originalHome, ct);
    }

    /// <summary>Associates one disabled reusable with its privately observed Home draft. Acquire SQL first,
    /// Home second; no actor/resource/permission reads under the retained Home lease. Dispose before audit.
    /// A publication approval cannot substitute for this separate definition approval.</summary>
    public Task<AutomationDefinitionCommitResult> AssociatePublishedDraftAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, HomeGraphPublicationObservation observation,
        HomeGraphSqlAssociationLeaseSource originalHome, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation); ArgumentNullException.ThrowIfNull(originalHome);
        return WriteGraphPublicationAsync(change, admission, observation, originalHome, null, ct);
    }

    private async Task<AutomationDefinitionCommitResult> WriteGraphPublicationAsync(AutomationDefinitionChange change,
        IAutomationDefinitionCommitAdmission admission, HomeGraphPublicationObservation? observation,
        HomeGraphSqlAssociationLeaseSource? originalHome, HomeAutomationDefinitionCommitFenceSource? preparationHome, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change); ArgumentNullException.ThrowIfNull(admission);
        AutomationDefinitionCommitResult Denied(string code) => new(false, code, null, change.OperationID, change.PayloadSHA256);
        var actualAuthority = ownerAuthority ?? ownerAuthorityAccessor?.Invoke();
        if (actualAuthority is null || !actualAuthority.Issued(admission, factory) || change.RequiresLinkedCommit ||
            change.ChangeKind != AutomationDefinitionChangeKind.Update || change.EntityKind != AutomationOwnerEntityKind.ReusableTask ||
            change.ReusableTask is not { } proposed || proposed.IsEnabled || proposed.ArchivedAt is not null ||
            proposed.OperationalState == AutomationOperationalState.Ready || change.ExpectedRevision < 1 || change.ExpectedRevision == long.MaxValue ||
            proposed.Id != change.EntityID || proposed.PublicationJournal is not { } next)
            return Denied("PublicationOwnerUnavailable");
        if (observation is null && (preparationHome is null || !preparationHome.IsFor(actualAuthority, factory)))
            return Denied("PublicationFinalClaimFenceUnavailable");
        await using var connection = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, ct, transaction).ConfigureAwait(false);
        if (identity.StoreId != change.StoreID) return Denied("OriginalStoreChanged");
        AutomationOwnerRead<ReusableTaskDefinition>? current;
        await using (var inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction;
            inspect.CommandText = "SELECT * FROM reusable_tasks WHERE lower(replace(replace(replace(id,'-',''),'{',''),'}',''))=lower(replace($id,'-',''));";
            inspect.Parameters.AddWithValue("$id", change.EntityID.ToString("D"));
            await using var reader = await inspect.ExecuteReaderAsync(ct).ConfigureAwait(false);
            current = await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadOwnedReusableTask(reader) : null;
            if (await reader.ReadAsync(ct).ConfigureAwait(false)) return Denied("AmbiguousCanonicalDefinitionIdentity");
        }
        if (current is null || current.RequiresRecovery || current.Value.Revision != change.ExpectedRevision ||
            current.Value.IsEnabled || current.Value.ArchivedAt is not null || current.Value.OwnerBinding is not { } owner ||
            owner.StoreId != identity.StoreId || owner.ProfileId != change.OriginalActor.ProfileId ||
            owner.AccountId != change.OriginalActor.AccountId || owner.OrganisationId != change.OriginalActor.OrganisationId)
            return Denied("OriginalDefinitionChanged");
        // This dedicated workflow changes only journal/binding. No name/metadata/owner/receipt substitution is admitted.
        var normalized = proposed with { GraphBinding = current.Value.GraphBinding, PublicationJournal = current.Value.PublicationJournal };
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(normalized), JsonSerializer.SerializeToElement(current.Value)))
            return Denied("PublicationPayloadChanged");
        if (!ValidPublicationTransition(current.Value, proposed, observation, change)) return Denied("PublicationTransitionUnavailable");
        var actor = change.OriginalActor;
        var actualOwner = new AutomationOwnerBinding(identity.StoreId, actor.ProfileId, actor.ActorId,
            actor.AuthenticationRevision, actor.AccountId, actor.OrganisationId);
        var context = new AutomationDefinitionCommitContext(identity, change.EntityID, change.EntityKind,
            change.ExpectedRevision, change.OperationID, change.PayloadSHA256, change.ActionID, actualOwner);
        if (!await admission.CheckAsync(context, ct).ConfigureAwait(false)) return Denied("OwnerAdmissionChanged");
        // Capture the exact original private claim BEFORE acquiring Home; never accept an arbitrary admission.
        var claim = actualAuthority.CaptureAssociationClaim(admission, factory, context);
        if (claim is null || observation is not null && originalHome is null) return Denied("PublicationClaimUnavailable");
        // SQL is already acquired. Capture trusted raw Home ownership/claim outside its retained lease.
        await using var preparedFence = observation is null
            ? await preparationHome!.CaptureAsync(actualAuthority, admission, factory, context, change.OriginalActor, ct).ConfigureAwait(false)
            : null;
        if (observation is null && (preparedFence is null || !actualAuthority.MatchesAssociationClaim(admission, factory, context, claim!) ||
            !await preparedFence.ValidateAsync(ct).ConfigureAwait(false))) return Denied("PublicationHomeChanged");
        await using var home = observation is null ? null :
            await originalHome!.AcquireAsync(observation, new[] { claim! }, ct).ConfigureAwait(false);
        if (observation is not null && (home is null || !actualAuthority.MatchesAssociationClaim(admission, factory, context, claim!) ||
            !await home.IsCurrentAsync(ct).ConfigureAwait(false))) return Denied("PublicationHomeChanged");
        var revision = checked(change.ExpectedRevision + 1); var now = DateTimeOffset.UtcNow;
        var receipt = new AutomationOwnerCommitReceipt(1, identity.StoreId, change.EntityID, AutomationDefinitionEntityKind.ReusableTask,
            change.OperationID, change.PayloadSHA256, change.ExpectedRevision, revision, now, actualOwner);
        await using var write = connection.CreateCommand(); write.Transaction = transaction;
        write.CommandText = """
            UPDATE reusable_tasks SET revision=$revision,updated_at=$now,is_enabled=0,operational_state=$state,
            owner_binding_json=$owner,graph_binding_json=$graph,publication_journal_json=$journal,owner_commit_receipt_json=$receipt
            WHERE id=$id AND revision=$expected AND is_enabled=0 AND archived_at IS NULL;
            """;
        write.Parameters.AddWithValue("$id", current.RetainedProtectedDescriptors["id"]!);
        write.Parameters.AddWithValue("$expected", change.ExpectedRevision); write.Parameters.AddWithValue("$revision", revision);
        write.Parameters.AddWithValue("$now", now.ToString("O")); write.Parameters.AddWithValue("$state", (int)AutomationOperationalState.NeedsAttention);
        write.Parameters.AddWithValue("$owner", JsonSerializer.Serialize(actualOwner));
        write.Parameters.AddWithValue("$graph", proposed.GraphBinding is null ? DBNull.Value : JsonSerializer.Serialize(proposed.GraphBinding));
        write.Parameters.AddWithValue("$journal", JsonSerializer.Serialize(next)); write.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt));
        if (await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) return Denied("RevisionConflict");
        // Every branch now retains its SAME actual Home lease across canonical SQL publication.
        // Neither branch recursively invokes CheckAsync/actor/receipt/resource resolution while Home is held.
        if (!actualAuthority.MatchesAssociationClaim(admission, factory, context, claim!) ||
            (preparedFence is not null ? !await preparedFence.ValidateAsync(ct).ConfigureAwait(false) :
                home is null || !await home.IsCurrentAsync(ct).ConfigureAwait(false))) return Denied("PublicationHomeChanged");
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new(true, observation is null ? "PublicationPrepared" : "PublishedDraftAssociated", revision,
            change.OperationID, change.PayloadSHA256);
    }

    private static bool ValidPublicationTransition(ReusableTaskDefinition current, ReusableTaskDefinition proposal,
        HomeGraphPublicationObservation? observation, AutomationDefinitionChange change)
    {
        var next = proposal.PublicationJournal!;
        if (next.OperationId == Guid.Empty || next.GraphId == Guid.Empty || next.RecoveryReason is not null ||
            next.OriginalPayloadSha256 is not { Length: 64 } digest || !digest.All(Uri.IsHexDigit)) return false;
        if (observation is null)
            return next.Phase == AutomationGraphPublicationPhase.Prepared && next.ExpectedDefinitionRevision == current.Revision &&
                next.PublishedDraftRevision is null && next.PublishedActiveRevision is null && proposal.GraphBinding == current.GraphBinding &&
                (current.PublicationJournal is null || current.PublicationJournal.Phase == AutomationGraphPublicationPhase.DefinitionCommitted) &&
                (current.GraphBinding is null || current.GraphBinding.GraphId == next.GraphId);
        var prior = current.PublicationJournal;
        var receipt = observation.Receipt;
        return prior is { Phase: AutomationGraphPublicationPhase.Prepared } &&
            prior.ExpectedDefinitionRevision == current.Revision - 1 && next == (prior with
            { Phase = AutomationGraphPublicationPhase.DefinitionCommitted, PublishedDraftRevision = receipt.CommittedGraphRevision }) &&
            prior.OriginalPayloadSha256 == receipt.ArgumentsDigest &&
            receipt.Kind == GraphPublicationKind.SaveDraft && receipt.OperationId == prior.OperationId && receipt.GraphId == prior.GraphId &&
            receipt.Owner.AppId == "automations" && receipt.Owner.StoreId == change.StoreID &&
            receipt.Owner.EntityKind == "automation.reusable-task" && receipt.Owner.EntityId == change.EntityID &&
            receipt.ExpectedGraphRevision != long.MaxValue && receipt.CommittedGraphRevision == receipt.ExpectedGraphRevision + 1 &&
            proposal.GraphBinding == new AutomationDefinitionGraphBinding(receipt.GraphId, receipt.CommittedGraphRevision, current.GraphBinding?.ActiveRevision) &&
            (current.GraphBinding is null ? receipt.ExpectedGraphRevision == 0 : receipt.ExpectedGraphRevision == current.GraphBinding.DraftRevision);
    }
}
