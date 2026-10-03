using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class PlannerRepository : IPlannerJourneySnapshotSource
{
    public async Task<PlannerJourneySnapshot?> ReadJourneySnapshotAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("A canonical Planner event identity is required.", nameof(eventId));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: true);
        var identity = await SqliteDatabase.ReadStoreIdentityAsync(connection, false, cancellationToken, transaction).ConfigureAwait(false);
        var plannerEvent = await GetEventAsync(connection, transaction, eventId, cancellationToken).ConfigureAwait(false);
        if (plannerEvent is null) return null;
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT * FROM planner_calendars WHERE id=$id;";
        command.Parameters.AddWithValue("$id", plannerEvent.CalendarId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new(identity, plannerEvent, ReadCalendar(reader));
    }
}
