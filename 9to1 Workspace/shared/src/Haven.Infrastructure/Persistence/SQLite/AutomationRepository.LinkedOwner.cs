using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class AutomationRepository
{
    /// <summary>One actual SQL transaction for an exact individually reviewed linked disable/archive pair.
    /// Both consumed capabilities still require separate fixed outcome/audit completion by the owning operation.
    /// This writer never publishes a graph, enables a run, or claims Home/SQL global atomicity.</summary>
    public async Task<AutomationLinkedDefinitionCommitResult> CompareExchangeLinkedPairAsync(
        AutomationLinkedDefinitionChange pair, WorkspaceStateRepository actualWorkspace,
        IAutomationDefinitionCommitAdmission reusableAdmission, IAutomationDefinitionCommitAdmission scheduleAdmission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pair); ArgumentNullException.ThrowIfNull(actualWorkspace);
        ArgumentNullException.ThrowIfNull(reusableAdmission); ArgumentNullException.ThrowIfNull(scheduleAdmission);
        AutomationLinkedDefinitionCommitResult Denied(string code) => new(false, code, pair.PairID, null, null);
        if (!ValidLinkedPair(pair) || !actualWorkspace.UsesConnectionFactory(factory)) return Denied("LinkedOwnerUnavailable");
        // Resolve and seal BOTH actual singleton issuer turns BEFORE acquiring SQL. Nothing resolves Home/DI under SQL.
        var taskTurn = actualWorkspace.CaptureOwnerWriteTurn(reusableAdmission);
        var scheduleTurn = CaptureOwnerWriteTurn(scheduleAdmission);
        if (taskTurn is null || scheduleTurn is null) return Denied("LinkedOwnerUnavailable");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var task = await actualWorkspace.ApplyOwnedTaskWithinTransactionAsync(pair.ReusableChange, reusableAdmission,
            taskTurn, connection, transaction, true, cancellationToken).ConfigureAwait(false);
        if (!task.Committed) return Denied(task.Code); // Disposal rolls back every candidate write.
        var schedule = await ApplyOwnedDefinitionWithinTransactionAsync(pair.ScheduleChange, scheduleAdmission,
            scheduleTurn, connection, transaction, true, cancellationToken).ConfigureAwait(false);
        if (!schedule.Committed) return Denied(schedule.Code);
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, cancellationToken, transaction).ConfigureAwait(false);
        if (identity.StoreId != pair.ReusableChange.StoreID) return Denied("OriginalStoreChanged");
        // The second write may await admission. Recheck BOTH original raw receipts/actors at the final pair boundary.
        if (!await reusableAdmission.CheckAsync(Context(identity, pair.ReusableChange), cancellationToken).ConfigureAwait(false) ||
            !await scheduleAdmission.CheckAsync(Context(identity, pair.ScheduleChange), cancellationToken).ConfigureAwait(false))
            return Denied("LinkedOwnerAdmissionChanged");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(true, "LinkedDefinitionsCommitted", pair.PairID, task, schedule);
    }
    private static AutomationDefinitionCommitContext Context(ResourceStoreIdentity identity, AutomationDefinitionChange change)
    {
        var actor = change.OriginalActor;
        var owner = new AutomationOwnerBinding(identity.StoreId, actor.ProfileId, actor.ActorId,
            actor.AuthenticationRevision, actor.AccountId, actor.OrganisationId);
        return new(identity, change.EntityID, change.EntityKind, change.ExpectedRevision, change.OperationID,
            change.PayloadSHA256, change.ActionID, owner);
    }
    private static bool ValidLinkedPair(AutomationLinkedDefinitionChange pair)
    {
        var task = pair.ReusableChange; var schedule = pair.ScheduleChange;
        if (task is null || schedule is null || !task.RequiresLinkedCommit || !schedule.RequiresLinkedCommit ||
            task.EntityKind != AutomationOwnerEntityKind.ReusableTask || schedule.EntityKind != AutomationOwnerEntityKind.Automation ||
            task.StoreID != schedule.StoreID || task.OriginalActor != schedule.OriginalActor ||
            task.EntityID != schedule.EntityID || task.ChangeKind != schedule.ChangeKind ||
            task.ChangeKind is not (AutomationDefinitionChangeKind.Disable or AutomationDefinitionChangeKind.Archive) ||
            task.OperationID == schedule.OperationID || pair.PairID == Guid.Empty || pair.PairPayloadSHA256 is not { Length: 64 })
            return false;
        var left = task.Arguments; var right = schedule.Arguments;
        if (!left.TryGetProperty("linkedEffect", out var effect) || !right.TryGetProperty("linkedEffect", out var other) ||
            effect.ValueKind != JsonValueKind.Object || !JsonElement.DeepEquals(effect, other)) return false;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(effect);
        if (!StringComparer.Ordinal.Equals(pair.PairPayloadSHA256, Convert.ToHexString(SHA256.HashData(bytes)))) return false;
        // These exact fields were sealed by CaptureLinkedPair; public pair wrappers cannot substitute an operation.
        return effect.GetProperty("pairID").GetGuid() == pair.PairID &&
            effect.GetProperty("actualStoreID").GetGuid() == task.StoreID &&
            effect.GetProperty("reusableOperation").GetGuid() == task.OperationID &&
            effect.GetProperty("scheduleOperation").GetGuid() == schedule.OperationID &&
            effect.GetProperty("expectedReusableRevision").GetInt64() == task.ExpectedRevision &&
            effect.GetProperty("expectedScheduleRevision").GetInt64() == schedule.ExpectedRevision &&
            effect.GetProperty("kind").Deserialize<AutomationDefinitionChangeKind>() == task.ChangeKind &&
            effect.GetProperty("originalActor").Deserialize<AuthenticatedResourceActor>() == task.OriginalActor &&
            JsonElement.DeepEquals(effect.GetProperty("reusable"), JsonSerializer.SerializeToElement(task.ReusableTask)) &&
            JsonElement.DeepEquals(effect.GetProperty("schedule"), JsonSerializer.SerializeToElement(schedule.Automation)) &&
            task.Scopes.SequenceEqual(schedule.Scopes) && task.Scopes.Count == 2;
    }
}
