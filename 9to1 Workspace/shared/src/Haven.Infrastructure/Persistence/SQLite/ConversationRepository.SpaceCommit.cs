using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class ConversationRepository : IConversationSpaceCommitStore
{
    /// <summary>One actual SQLite writer transaction: exact row compare, current authority at admission
    /// and immediately before commit. This does not make another settings/Home store atomic with SQL.</summary>
    public async Task<ConversationSpaceCommitResult> CompareExchangeSpaceAsync(Guid expectedStoreId,
        IReadOnlyList<ConversationSpaceChange> changes, IConversationSpaceCommitAdmission admission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes); ArgumentNullException.ThrowIfNull(admission);
        var captured = Array.AsReadOnly(changes.ToArray());
        if (expectedStoreId == Guid.Empty || captured.Count is < 1 or > 1000 ||
            captured.Any(change => change is null || change.Proposed is null || change.Proposed.Id == Guid.Empty ||
                change.Proposed.SpaceId == Guid.Empty || string.IsNullOrWhiteSpace(change.Proposed.Title) || change.Proposed.Title.Length > 4096 ||
                !Enum.IsDefined(change.Proposed.Mode) || !Enum.IsDefined(change.Proposed.Kind) ||
                change.Proposed.UpdatedAt < change.Proposed.CreatedAt ||
                change.Expected is { } expected && (expected.Id != change.Proposed.Id ||
                    (expected with { SpaceId = change.Proposed.SpaceId, UpdatedAt = change.Proposed.UpdatedAt }) != change.Proposed)) ||
            captured.Select(change => change.Proposed.Id).Distinct().Count() != captured.Count)
            throw new ArgumentException("Space assignment requires unique immutable canonical rows; existing content cannot be overwritten.", nameof(changes));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Microsoft.Data.Sqlite's default serializable transaction acquires the writer lease (not deferred).
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, cancellationToken, transaction).ConfigureAwait(false);
        if (identity.StoreId != expectedStoreId) return new(ConversationSpaceCommitStatus.StoreMismatch);
        foreach (var change in captured)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT * FROM conversations WHERE id=$id;";
            command.Parameters.AddWithValue("$id", change.Proposed.Id.ToString());
            var current = (await ReadConversationsAsync(command, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
            if (current != change.Expected) return new(ConversationSpaceCommitStatus.RevisionConflict);
        }
        if (!await admission.CheckAsync(new(identity, captured, ConversationSpaceCommitPhase.Admission), cancellationToken).ConfigureAwait(false))
            return new(ConversationSpaceCommitStatus.AdmissionRejected);
        foreach (var change in captured)
            await UpsertCoreAsync(connection, transaction, change.Proposed, cancellationToken).ConfigureAwait(false);
        if (!await admission.CheckAsync(new(identity, captured, ConversationSpaceCommitPhase.Publication), cancellationToken).ConfigureAwait(false))
            return new(ConversationSpaceCommitStatus.AdmissionRejected);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(ConversationSpaceCommitStatus.Committed);
    }
}
