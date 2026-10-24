using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Finite historical lifecycle reads over the SAME protected SQLite
/// ledger. This does not open the repository's unprotected connection factory,
/// initialise a schema, disclose payloads or grant execution authority.</summary>
public sealed partial class CanonicalAutomationLibraryOriginalReadOwner : ICanonicalAutomationRunOriginalReadSource
{
    private readonly ConditionalWeakTable<ICanonicalAutomationRunOriginalObservation, RunObservation> _runObservations = new();
    private readonly ConditionalWeakTable<ICanonicalAutomationRunOriginalContinuation, RunContinuation> _runContinuations = new();
    private sealed class RunContinuation(CanonicalAutomationLibraryOriginalReadOwner owner,
        ICanonicalAutomationLibraryOriginalObservation page, AutomationOwnerRead<AutomationDefinition> row,
        AuthenticatedResourceActor actor, int limit, string scheduledFor, string id)
        : ICanonicalAutomationRunOriginalContinuation
    {
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Owner = owner;
        internal readonly ICanonicalAutomationLibraryOriginalObservation Page = page;
        internal readonly AutomationOwnerRead<AutomationDefinition> Row = row;
        internal readonly AuthenticatedResourceActor Actor = actor;
        internal readonly int Limit = limit;
        internal readonly string ScheduledFor = scheduledFor, Id = id;
    }
    private sealed record RunObservation(AuthenticatedResourceActor Actor, ResourceStoreIdentity OriginalStoreIdentity,
        Guid AutomationId, CanonicalAutomationLibraryReadState State, string Detail,
        IReadOnlyList<CanonicalAutomationRunSummary> Runs, ICanonicalAutomationRunOriginalContinuation? NextContinuation)
        : ICanonicalAutomationRunOriginalObservation;
    private sealed record StoredRun(CanonicalAutomationRunSummary Summary, string ScheduledFor, string Id);

    public bool IsIssuedOriginalRunObservation(ICanonicalAutomationRunOriginalObservation actual) =>
        actual is RunObservation value && _runObservations.TryGetValue(actual, out var same) && ReferenceEquals(same, value);
    public bool IsIssuedOriginalRunContinuation(ICanonicalAutomationRunOriginalContinuation actual) =>
        actual is RunContinuation value && ReferenceEquals(value.Owner, this) &&
        _runContinuations.TryGetValue(actual, out var same) && ReferenceEquals(same, value);

    public Task<ICanonicalAutomationRunOriginalObservation> ReadOriginalRunsWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation samePage, AutomationOwnerRead<AutomationDefinition> sameRow,
        AuthenticatedResourceActor actualActor, int limit, Action<Action> scope, Action<Task> retain,
        CancellationToken token, ICanonicalAutomationRunOriginalContinuation? continuation = null)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader<ICanonicalAutomationRunOriginalObservation>(source, async () =>
        {
            var selection = source.Invoke(() =>
            {
                if (limit is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(limit));
                var selected = ObserveOriginalWriteSelection(samePage, sameRow);
                if (selected.Observation.Actor != actualActor)
                    throw new UnauthorizedAccessException("The selected history requires the same current Home actor.");
                return selected;
            });
            var original = (Observation)selection.Observation;
            var identity = original.OriginalStoreIdentity!;
            var receipt = selection.Ownership;
            var window = source.Invoke(() => continuation is null ? null :
                IsIssuedOriginalRunContinuation(continuation) && continuation is RunContinuation cursor &&
                ReferenceEquals(cursor.Page, samePage) && ReferenceEquals(cursor.Row, sameRow) &&
                cursor.Actor == actualActor && cursor.Limit == limit ? cursor :
                    throw new UnauthorizedAccessException("Use the SAME selected automation's source-issued history continuation."));
            await source.Read(() => RevalidateOriginalObservationWithinSourceAsync(original, actualActor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await DemandActor(actualActor, source, token).ConfigureAwait(false);
            await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);

            CanonicalSqliteOriginalStoreLease? lease = null;
            var failures = new List<Exception>();
            List<StoredRun> rows = []; bool available = false;
            try
            {
                await source.ReadCapture(() => _store.AcquireOriginalProtectedReadWithinSourceAsync(actualActor, false,
                    source.Run, source.Retain, token), actual => lease = actual).ConfigureAwait(false);
                source.Run(() =>
                {
                    if (!_store.IsIssuedOriginalLease(lease!) || lease!.OriginalIdentity != identity)
                        throw new UnauthorizedAccessException("The SAME protected ledger lease and store identity are required.");
                });
                await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
                await source.JoinAllAsync().ConfigureAwait(false);
                var types = await QueryRows(lease!, command => command.CommandText =
                    "SELECT type FROM sqlite_master WHERE name='automation_runs';", reader => reader.GetString(0), 2, token).ConfigureAwait(false);
                if (types.Count > 1 || types.Any(type => type != "table"))
                    throw new InvalidDataException("The run ledger name does not identify one actual table.");
                available = types.Count == 1;
                if (available) rows = await ReadRunPage(lease!, sameRow, limit, window, token).ConfigureAwait(false);
                await source.Read(() => lease!.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
            }
            catch (Exception cause) { failures.Add(cause); }
            Task? close = null;
            if (lease is not null)
                try { close = lease.CloseAndDrainAsync(); source.Retain(close); await close.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, close, cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            CanonicalSqliteOriginalStoreOwner.Throw(failures);
            // The run transaction is closed before publication. Reobserve the
            // selected definition, Home receipt and actor; a retired page cannot
            // lend its historical READ approval to this new result.
            await source.Read(() => RevalidateOriginalObservationWithinSourceAsync(original, actualActor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await DemandActor(actualActor, source, token).ConfigureAwait(false);
            await DemandReceipt(receipt, identity, actualActor, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            RunContinuation? next = rows.Count > limit && rows[limit - 1] is { } last
                ? new(this, samePage, sameRow, actualActor, limit, last.ScheduledFor, last.Id) : null;
            var result = new RunObservation(actualActor, identity, sameRow.Value.Id,
                available ? CanonicalAutomationLibraryReadState.Available : CanonicalAutomationLibraryReadState.SetupRequired,
                available ? "Saved run lifecycle facts from the original ledger. Payloads and execution controls are not included."
                    : "The saved run ledger is unavailable. This read does not initialise or replace it.",
                Array.AsReadOnly(rows.Take(limit).Select(row => row.Summary).ToArray()), next);
            source.Run(() =>
            {
                if (next is not null) _runContinuations.Add(next, next);
                _runObservations.Add(result, result);
            });
            return result;
        });
    }

    private static async Task<List<StoredRun>> ReadRunPage(CanonicalSqliteOriginalStoreLease lease,
        AutomationOwnerRead<AutomationDefinition> row, int limit, RunContinuation? window, CancellationToken token)
    {
        var columns = await QueryRows(lease, command => command.CommandText = "PRAGMA table_info(automation_runs);",
            reader => reader.GetString(1), 256, token).ConfigureAwait(false);
        string[] required = ["id", "automation_id", "status", "scheduled_for", "started_at", "completed_at", "result", "error"];
        if (required.Any(name => !columns.Contains(name, StringComparer.Ordinal)))
            throw new InvalidDataException("The saved run ledger schema is incomplete; READ will not migrate it.");
        bool Has(string name) => columns.Contains(name, StringComparer.Ordinal);
        string Optional(string name) => Has(name) ? name : "NULL AS " + name;
        string Presence(string name) => Has(name) ? name + " IS NOT NULL AS has_" + name : "NULL AS has_" + name;
        // Preserve the actual source-owned SQLite key, including its case. An
        // equivalent Guid string is not a replacement for the observed row key.
        if (!row.RetainedProtectedDescriptors.TryGetValue("id", out var rawId) || rawId is null)
            throw new InvalidDataException("The selected definition's actual stored key is unavailable.");
        using var descriptor = JsonDocument.Parse(rawId);
        var rawKey = descriptor.RootElement.GetProperty("value").GetString();
        if (descriptor.RootElement.GetProperty("storageClass").GetString() != "text" ||
            !Guid.TryParse(rawKey, out var actualId) || actualId != row.Value.Id)
            throw new InvalidDataException("The selected definition's actual stored key is inconsistent.");
        var pageBytes = 0;
        return await QueryRows(lease, command =>
        {
            command.CommandText = "SELECT id,automation_id,status,scheduled_for,started_at,completed_at," +
                "result IS NOT NULL AS has_result,error IS NOT NULL AS has_error," +
                string.Join(",", new[] { Optional("revision"), Optional("definition_revision"), Optional("pinned_graph_json"),
                    Presence("details_json"), Presence("continuation_descriptor_json") }) +
                " FROM automation_runs WHERE automation_id=$automation" + (window is null ? "" :
                    " AND (scheduled_for < $scheduled OR (scheduled_for=$scheduled AND id>$id))") +
                " ORDER BY scheduled_for DESC,id ASC LIMIT $maximum;";
            command.Parameters.AddWithValue("$automation", rawKey!);
            command.Parameters.AddWithValue("$maximum", limit + 1);
            if (window is not null)
            {
                command.Parameters.AddWithValue("$scheduled", window.ScheduledFor);
                command.Parameters.AddWithValue("$id", window.Id);
            }
        }, reader =>
        {
            var id = reader.String("id"); var scheduled = reader.String("scheduled_for");
            var pinnedJson = reader.NullableString("pinned_graph_json");
            var bytes = Encoding.UTF8.GetByteCount(id) + Encoding.UTF8.GetByteCount(scheduled) +
                (pinnedJson is null ? 0 : Encoding.UTF8.GetByteCount(pinnedJson));
            if (bytes > 8192 || checked(pageBytes += bytes) > 256 * 1024)
                throw new InvalidDataException("The saved run lifecycle page exceeds its bounded READ size.");
            var pinned = pinnedJson is null ? null : JsonSerializer.Deserialize<AutomationGraphBinding>(pinnedJson)
                ?? throw new InvalidDataException("The stored run graph binding is null rather than an identified revision.");
            long? Number(string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt64(reader.GetOrdinal(name));
            bool? Flag(string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.Boolean(name);
            var summary = new CanonicalAutomationRunSummary(reader.Guid("id"), reader.Guid("automation_id"),
                (AutomationRunStatus)reader.Int32("status"), reader.DateTimeOffset("scheduled_for"),
                reader.NullableDateTimeOffset("started_at"), reader.NullableDateTimeOffset("completed_at"),
                Number("revision"), Number("definition_revision"), pinned, reader.Boolean("has_result"),
                reader.Boolean("has_error"), Flag("has_details_json"), Flag("has_continuation_descriptor_json"));
            if (summary.AutomationId != row.Value.Id)
                throw new InvalidDataException("The saved run belongs to a different automation.");
            return new StoredRun(summary, scheduled, id);
        }, limit + 1, token).ConfigureAwait(false);
    }
}
