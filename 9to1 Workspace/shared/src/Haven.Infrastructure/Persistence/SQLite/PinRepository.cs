/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/PinRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns PinRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents pin repository and keeps its related state and behavior together.
/// </summary>
public sealed class PinRepository(ISqliteConnectionFactory factory) : IOrderedPinRepository
{
    /// <summary>
    /// Retrieves pins async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ModePin>> GetPinsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadPinsAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ModePin>> ReadPinsAsync(SqliteConnection connection,
        SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM mode_pins ORDER BY sort_order;";
        var results = new List<ModePin>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(new ModePin(
                Guid.Parse(reader.String("id")),
                Guid.Parse(reader.String("mode_id")),
                reader.Int32("sort_order"),
                reader.DateTimeOffset("pinned_at")));
        return results;
    }

    /// <summary>
    /// Performs upsert pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertPinAsync(ModePin pin, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mode_pins(id, mode_id, sort_order, pinned_at)
            VALUES($id, $modeId, $sortOrder, $pinnedAt)
            ON CONFLICT(mode_id) DO UPDATE SET sort_order=$sortOrder, pinned_at=$pinnedAt;
            """;
        command.Parameters.AddWithValue("$id", pin.Id.ToString());
        command.Parameters.AddWithValue("$modeId", pin.ModeId.ToString());
        command.Parameters.AddWithValue("$sortOrder", pin.SortOrder);
        command.Parameters.AddWithValue("$pinnedAt", pin.PinnedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeletePinAsync(Guid modeId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mode_pins WHERE mode_id=$modeId;";
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryReorderAsync(IReadOnlyList<ModePin> expected, IReadOnlyList<Guid> orderedModeIds,
        CancellationToken cancellationToken, Func<CancellationToken, Task>? admitCommit = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(orderedModeIds);
        if (expected.Any(pin => pin is null || pin.ModeId == Guid.Empty || pin.Id == Guid.Empty) ||
            expected.Select(pin => pin.ModeId).Distinct().Count() != expected.Count ||
            orderedModeIds.Count != expected.Count || orderedModeIds.Distinct().Count() != expected.Count ||
            !orderedModeIds.ToHashSet().SetEquals(expected.Select(pin => pin.ModeId)))
            throw new ArgumentException("Reordering must retain every current canonical pin exactly once.");

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = connection.BeginTransaction(deferred: false);
        var actual = await ReadPinsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!actual.OrderBy(pin => pin.ModeId).SequenceEqual(expected.OrderBy(pin => pin.ModeId))) return false;

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE mode_pins SET sort_order=$order WHERE mode_id=$mode;";
        var order = update.Parameters.Add("$order", Microsoft.Data.Sqlite.SqliteType.Integer);
        var mode = update.Parameters.Add("$mode", Microsoft.Data.Sqlite.SqliteType.Text);
        for (var index = 0; index < orderedModeIds.Count; index++)
        {
            order.Value = index;
            mode.Value = orderedModeIds[index].ToString();
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The canonical pin changed during its ordering transaction.");
        }
        if (admitCommit is not null) await admitCommit(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TrySetPinnedAsync(IReadOnlyList<ModePin> expected, Guid modeId, bool pinned,
        CancellationToken cancellationToken, Func<CancellationToken, Task>? admitCommit = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (modeId == Guid.Empty || expected.Any(pin => pin is null || pin.Id == Guid.Empty || pin.ModeId == Guid.Empty) ||
            expected.Select(pin => pin.ModeId).Distinct().Count() != expected.Count)
            throw new ArgumentException("Retain the current canonical pin identities.");
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = connection.BeginTransaction(deferred: false);
        var actual = await ReadPinsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!actual.OrderBy(pin => pin.ModeId).SequenceEqual(expected.OrderBy(pin => pin.ModeId))) return false;
        var exists = actual.Any(pin => pin.ModeId == modeId);
        if (exists != pinned)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (pinned)
            {
                command.CommandText = "INSERT INTO mode_pins(id, mode_id, sort_order, pinned_at) VALUES($id, $mode, $order, $at);";
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("$order", actual.Count == 0 ? 0 : checked(actual.Max(pin => pin.SortOrder) + 1));
                command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            }
            else command.CommandText = "DELETE FROM mode_pins WHERE mode_id=$mode;";
            command.Parameters.AddWithValue("$mode", modeId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The canonical pin changed during its membership transaction.");
        }
        if (admitCommit is not null) await admitCommit(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
